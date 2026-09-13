"""VIP routes: status, catálogo de planos, histórico e compra com WCoin.

Replica em SQL as mesmas regras do jogo (src/GameLogic/VipService.cs e WCoinService.cs),
já que o site e o servidor compartilham o mesmo banco Postgres (schema "data"):

- VIP ativo  ⇔  data."AccountVip"."ExpiresAt" existe e é > agora (UTC).
- A compra bloqueia se já houver VIP ativo (não acumula nem renova por cima).
- O débito é atômico: valida saldo, debita a carteira, grava o extrato (Type = Purchase),
  faz upsert do AccountVip e insere no VipHistory — tudo numa única transação.

O multiplicador de drop exibido vem do banco (data."VipPlan"."DropChanceMultiplier"), que é
o dado persistido real. O preço vem de config.VIP_PRICES (a tabela VipPlan não guarda preço).
"""
import logging
import math
import uuid
from datetime import datetime, timedelta, timezone

from flask import Blueprint, jsonify, request, session

from .. import db
from ..config import VIP_PRICES, WCOIN_TX_PURCHASE

logger = logging.getLogger(__name__)

bp = Blueprint('vip', __name__)


def _read_vip_status(cursor, account_id):
    """Lê o status VIP e o saldo WCoin da conta.

    Retorna ``(status_dict, wcoin_balance)``. ``status_dict`` espelha o VipStatusInfo do
    jogo: isActive, planName, expiresAt (ISO), daysRemaining (arredondado para cima),
    dropMultiplier e dropBonusPercent.
    """
    cursor.execute(
        '''
        SELECT av."VipPlanId", av."ExpiresAt", vp."Name", vp."DropChanceMultiplier"
        FROM "data"."AccountVip" av
        LEFT JOIN "data"."VipPlan" vp ON vp."Id" = av."VipPlanId"
        WHERE av."Id" = %s
        ''',
        (account_id,),
    )
    row = cursor.fetchone()

    cursor.execute('SELECT "Balance" FROM "data"."WCoinWallet" WHERE "Id" = %s', (account_id,))
    wallet = cursor.fetchone()
    balance = int(wallet[0]) if wallet and wallet[0] is not None else 0

    now = datetime.now(timezone.utc)
    if row and row[1] is not None and row[1] > now:
        multiplier = float(row[3]) if row[3] is not None else 1.0
        remaining_days = max(0, math.ceil((row[1] - now).total_seconds() / 86400))
        status = {
            'isActive': True,
            'planName': row[2],
            'expiresAt': row[1].isoformat(),
            'daysRemaining': remaining_days,
            'dropMultiplier': round(multiplier, 3),
            'dropBonusPercent': int(round((multiplier - 1.0) * 100)),
        }
    else:
        status = {
            'isActive': False,
            'planName': None,
            'expiresAt': row[1].isoformat() if row and row[1] is not None else None,
            'daysRemaining': 0,
            'dropMultiplier': 1.0,
            'dropBonusPercent': 0,
        }
    return status, balance


@bp.route('/api/vip/status', methods=['GET'])
def vip_status():
    """Status VIP atual da conta logada + saldo WCoin."""
    account_id = session.get('account_id')
    if not account_id:
        return jsonify({'success': False, 'message': 'Faça login para continuar'}), 401

    try:
        conn = db.get_db_connection()
        try:
            cursor = conn.cursor()
            status, balance = _read_vip_status(cursor, account_id)
        finally:
            conn.close()
        return jsonify({'success': True, 'vip': status, 'wcoinBalance': balance}), 200
    except Exception as e:
        logger.error(f"Error getting VIP status: {e}")
        return jsonify({'success': False, 'message': 'Erro ao carregar status VIP'}), 500


@bp.route('/api/vip/plans', methods=['GET'])
def vip_plans():
    """Catálogo de planos VIP vendáveis (com preço em WCoin)."""
    account_id = session.get('account_id')
    if not account_id:
        return jsonify({'success': False, 'message': 'Faça login para continuar'}), 401

    try:
        conn = db.get_db_connection()
        try:
            cursor = conn.cursor()
            cursor.execute(
                '''
                SELECT "Name", "DurationDays", "DropChanceMultiplier"
                FROM "data"."VipPlan"
                WHERE "IsActive" = true
                ORDER BY "DropChanceMultiplier"
                '''
            )
            rows = cursor.fetchall()
        finally:
            conn.close()

        plans = []
        for name, duration, multiplier in rows:
            price = VIP_PRICES.get(name)
            if price is None:
                # Plano sem preço mapeado não é vendável pelo site.
                continue
            mult = float(multiplier) if multiplier is not None else 1.0
            plans.append({
                'name': name,
                'durationDays': int(duration or 0),
                'dropMultiplier': round(mult, 3),
                'dropBonusPercent': int(round((mult - 1.0) * 100)),
                'price': price,
            })
        return jsonify({'success': True, 'plans': plans}), 200
    except Exception as e:
        logger.error(f"Error listing VIP plans: {e}")
        return jsonify({'success': False, 'message': 'Erro ao carregar planos VIP'}), 500


@bp.route('/api/vip/history', methods=['GET'])
def vip_history():
    """Histórico (append-only) de ativações de VIP da conta logada."""
    account_id = session.get('account_id')
    if not account_id:
        return jsonify({'success': False, 'message': 'Faça login para continuar'}), 401

    try:
        conn = db.get_db_connection()
        try:
            cursor = conn.cursor()
            cursor.execute(
                '''
                SELECT COALESCE(vp."Name", 'Desconhecido'), vh."StartedAt", vh."ExpiresAt", vh."Source"
                FROM "data"."VipHistory" vh
                LEFT JOIN "data"."VipPlan" vp ON vp."Id" = vh."VipPlanId"
                WHERE vh."AccountId" = %s
                ORDER BY vh."CreatedAt" DESC
                LIMIT 50
                ''',
                (account_id,),
            )
            rows = cursor.fetchall()
        finally:
            conn.close()

        history = [{
            'planName': r[0],
            'startedAt': r[1].isoformat() if r[1] else None,
            'expiresAt': r[2].isoformat() if r[2] else None,
            'source': r[3],
        } for r in rows]
        return jsonify({'success': True, 'history': history}), 200
    except Exception as e:
        logger.error(f"Error getting VIP history: {e}")
        return jsonify({'success': False, 'message': 'Erro ao carregar histórico VIP'}), 500


@bp.route('/api/vip/purchase', methods=['POST'])
def vip_purchase():
    """Compra/ativa um plano VIP debitando WCoin, numa única transação atômica."""
    account_id = session.get('account_id')
    if not account_id:
        return jsonify({'success': False, 'message': 'Faça login para continuar'}), 401

    data = request.get_json(silent=True) or {}
    plan_name = (data.get('plan') or '').strip()
    if not plan_name:
        return jsonify({'success': False, 'message': 'Informe o plano a comprar'}), 400

    conn = None
    try:
        conn = db.get_db_connection()
        cursor = conn.cursor()

        # 1. Resolve o plano no banco pelo nome (case-insensitive) e exige que esteja ativo.
        cursor.execute(
            '''
            SELECT "Id", "Name", "DurationDays"
            FROM "data"."VipPlan"
            WHERE lower("Name") = lower(%s) AND "IsActive" = true
            ''',
            (plan_name,),
        )
        plan = cursor.fetchone()
        if not plan:
            conn.rollback()
            return jsonify({'success': False, 'message': 'Plano VIP inválido ou indisponível'}), 400
        plan_id, canonical_name, duration_days = plan[0], plan[1], int(plan[2] or 0)

        price = VIP_PRICES.get(canonical_name)
        if price is None:
            conn.rollback()
            return jsonify({'success': False, 'message': 'Este plano não está à venda no site'}), 400

        # 2. Trava a carteira PRIMEIRO. Isso serializa cliques concorrentes da mesma conta e
        #    coordena com o próprio jogo (mesmo banco). Sem carteira ⇒ saldo 0.
        cursor.execute('SELECT "Balance" FROM "data"."WCoinWallet" WHERE "Id" = %s FOR UPDATE', (account_id,))
        wallet = cursor.fetchone()
        balance = int(wallet[0]) if wallet and wallet[0] is not None else 0

        now = datetime.now(timezone.utc)

        # 3. Bloqueia se já houver VIP ativo (mesma regra do jogo: não acumula/renova por cima).
        cursor.execute('SELECT "ExpiresAt" FROM "data"."AccountVip" WHERE "Id" = %s', (account_id,))
        current = cursor.fetchone()
        if current and current[0] is not None and current[0] > now:
            conn.rollback()
            return jsonify({
                'success': False,
                'message': 'Você já possui um VIP ativo. Aguarde ele expirar para ativar outro.',
                'expiresAt': current[0].isoformat(),
            }), 409

        # 4. Valida saldo.
        if balance < price:
            conn.rollback()
            return jsonify({
                'success': False,
                'message': f'Saldo insuficiente. Preço: {price} WCoin. Seu saldo: {balance}.',
            }), 402

        new_balance = balance - price
        expires_at = now + timedelta(days=duration_days)

        # 5. Debita a carteira e grava o extrato (Type = Purchase).
        cursor.execute(
            'UPDATE "data"."WCoinWallet" SET "Balance" = %s, "UpdatedAt" = %s WHERE "Id" = %s',
            (new_balance, now, account_id),
        )
        cursor.execute(
            '''
            INSERT INTO "data"."WCoinTransaction"
                ("Id", "AccountId", "Type", "Amount", "BalanceAfter", "Description", "CreatedAt")
            VALUES (%s, %s, %s, %s, %s, %s, %s)
            ''',
            (str(uuid.uuid4()), account_id, WCOIN_TX_PURCHASE, price, new_balance,
             f'Compra VIP {canonical_name} (site)', now),
        )

        # 6. Upsert do AccountVip (shared primary key com Account).
        cursor.execute(
            '''
            INSERT INTO "data"."AccountVip" ("Id", "VipPlanId", "ExpiresAt")
            VALUES (%s, %s, %s)
            ON CONFLICT ("Id") DO UPDATE
                SET "VipPlanId" = EXCLUDED."VipPlanId", "ExpiresAt" = EXCLUDED."ExpiresAt"
            ''',
            (account_id, plan_id, expires_at),
        )

        # 7. Registra no histórico (append-only).
        cursor.execute(
            '''
            INSERT INTO "data"."VipHistory"
                ("Id", "AccountId", "VipPlanId", "StartedAt", "ExpiresAt", "Source", "CreatedAt")
            VALUES (%s, %s, %s, %s, %s, %s, %s)
            ''',
            (str(uuid.uuid4()), account_id, plan_id, now, expires_at, 'WebPanel', now),
        )

        conn.commit()

        # Relê o estado já persistido para devolver ao cliente (nova transação implícita).
        status, new_bal = _read_vip_status(cursor, account_id)
        return jsonify({
            'success': True,
            'message': f'VIP {canonical_name} ativado com sucesso!',
            'vip': status,
            'wcoinBalance': new_bal,
        }), 200
    except Exception as e:
        if conn is not None:
            conn.rollback()
        logger.error(f"Error purchasing VIP: {e}")
        return jsonify({'success': False, 'message': 'Erro ao processar a compra do VIP'}), 500
    finally:
        if conn is not None:
            conn.close()
