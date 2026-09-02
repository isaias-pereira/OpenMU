"""Account routes: registration, login/logout, session and dashboard."""
import logging

from flask import Blueprint, jsonify, request, session

from .. import db
from ..accounts import create_account
from ..auth import get_account_by_login, get_session_account
from ..config import ADMIN_ACCOUNT_STATES, BANNED_ACCOUNT_STATES
from ..leveling import calculate_level_from_experience
from ..openmu import get_online_character_names
from ..security import check_password, validate_email, validate_login_name

logger = logging.getLogger(__name__)

bp = Blueprint('accounts', __name__)


@bp.route('/api/register', methods=['POST'])
def register():
    """API endpoint to register a new account."""
    try:
        data = request.get_json()

        if not data:
            return jsonify({'success': False, 'message': 'Dados inválidos'}), 400

        login_name = data.get('loginName', '').strip()
        password = data.get('password', '')
        email = data.get('email', '').strip().lower()
        security_code = data.get('securityCode', '').strip()

        if not login_name or not password or not email or not security_code:
            return jsonify({'success': False, 'message': 'Todos os campos são obrigatórios'}), 400

        if not validate_login_name(login_name):
            return jsonify({'success': False, 'message': 'Login deve ter entre 3 e 10 caracteres alfanuméricos'}), 400

        if len(password) < 6:
            return jsonify({'success': False, 'message': 'Senha deve ter pelo menos 6 caracteres'}), 400

        if not validate_email(email):
            return jsonify({'success': False, 'message': 'E-mail inválido'}), 400

        if len(security_code) < 4:
            return jsonify({'success': False, 'message': 'Código de segurança deve ter pelo menos 4 caracteres'}), 400

        account = create_account(login_name, password, email, security_code)

        return jsonify({
            'success': True,
            'message': 'Conta criada com sucesso',
            'account': account
        }), 201

    except ValueError as e:
        logger.warning(f"Validation error: {e}")
        return jsonify({'success': False, 'message': str(e)}), 409

    except RuntimeError as e:
        logger.error(f"Runtime error: {e}")
        return jsonify({'success': False, 'message': str(e)}), 500

    except Exception as e:
        logger.error(f"Unexpected error: {e}")
        return jsonify({'success': False, 'message': 'Erro interno do servidor'}), 500


@bp.route('/api/login', methods=['POST'])
def login():
    """Authenticate an account and start a browser session."""
    try:
        data = request.get_json() or {}
        login_name = data.get('loginName', '').strip()
        password = data.get('password', '')

        if not login_name or not password:
            return jsonify({'success': False, 'message': 'Informe login e senha'}), 400

        account = get_account_by_login(login_name)
        if not account or not check_password(password, account[2]):
            return jsonify({'success': False, 'message': 'Login ou senha inválidos'}), 401

        if account[4] in BANNED_ACCOUNT_STATES:
            return jsonify({'success': False, 'message': 'Esta conta está bloqueada'}), 403

        session.clear()
        session['account_id'] = str(account[0])
        return jsonify({'success': True, 'loginName': account[1]}), 200
    except Exception as e:
        logger.error(f"Login error: {e}")
        return jsonify({'success': False, 'message': 'Erro interno do servidor'}), 500


@bp.route('/api/logout', methods=['POST'])
def logout():
    session.clear()
    return jsonify({'success': True}), 200


@bp.route('/api/session', methods=['GET'])
def get_session_info():
    """Report whether the visitor is signed in and may open the staff panel."""
    account = get_session_account()
    return jsonify({
        'success': True,
        'loggedIn': bool(account),
        'loginName': account[0] if account else None,
        'isAdmin': bool(account) and account[1] in ADMIN_ACCOUNT_STATES
    }), 200


@bp.route('/api/me', methods=['GET'])
def account_dashboard():
    """Return the signed-in account and all of its character statistics."""
    account_id = session.get('account_id')
    if not account_id:
        return jsonify({'success': False, 'message': 'Faça login para continuar'}), 401

    try:
        conn = db.get_db_connection()
        try:
            cursor = conn.cursor()
            cursor.execute('''
                SELECT
                    a."LoginName",
                    a."EMail",
                    a."State",
                    c."Id",
                    c."Name",
                    COALESCE(cc."Name", '-') as class_name,
                    c."Experience",
                    c."MasterExperience",
                    c."PlayerKillCount",
                    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                        WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Resets' LIMIT 1), 0),
                    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                        WHERE sa."CharacterId" = c."Id" AND ad."Designation" IN ('MasterResets', 'Master Resets') LIMIT 1), 0),
                    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                        WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Strength' LIMIT 1), 0),
                    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                        WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Agility' LIMIT 1), 0),
                    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                        WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Vitality' LIMIT 1), 0),
                    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                        WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Energy' LIMIT 1), 0),
                    COALESCE(g."Name", '-')
                FROM "data"."Account" a
                LEFT JOIN "data"."Character" c ON c."AccountId" = a."Id"
                LEFT JOIN "config"."CharacterClass" cc ON cc."Id" = c."CharacterClassId"
                LEFT JOIN "guild"."GuildMember" gm ON gm."Id" = c."Id"
                LEFT JOIN "guild"."Guild" g ON g."Id" = gm."GuildId"
                WHERE a."Id" = %s
                ORDER BY c."CharacterSlot"
            ''', (account_id,))
            rows = cursor.fetchall()
        finally:
            conn.close()

        if not rows:
            return jsonify({'success': False, 'message': 'Conta não encontrada'}), 404

        online_names = get_online_character_names()

        characters = []
        for row in rows:
            if not row[4]:
                continue
            characters.append({
                'id': str(row[3]),
                'name': row[4],
                'className': row[5],
                'classCode': row[5],
                'guild': row[15],
                'level': calculate_level_from_experience(row[6] or 0),
                'masterLevel': calculate_level_from_experience(row[7] or 0),
                'kills': int(row[8] or 0),
                'resets': int(row[9] or 0),
                'masterResets': int(row[10] or 0),
                'strength': int(row[11] or 0),
                'agility': int(row[12] or 0),
                'vitality': int(row[13] or 0),
                'energy': int(row[14] or 0),
                'status': 'Online' if row[4] in online_names else 'Offline'
            })

        account_state = rows[0][2]
        is_admin = account_state in ADMIN_ACCOUNT_STATES
        role = 'GM' if is_admin else 'PLAYER'

        return jsonify({'success': True, 'account': {
            'loginName': rows[0][0],
            'email': rows[0][1],
            'role': role,
            'isAdmin': is_admin,
            'characters': characters
        }}), 200
    except Exception as e:
        logger.error(f"Error getting account dashboard: {e}")
        return jsonify({'success': False, 'message': 'Erro ao carregar painel da conta'}), 500
