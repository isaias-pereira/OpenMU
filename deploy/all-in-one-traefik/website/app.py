import os
import re
import uuid
import math
import logging
import json
import secrets
import urllib.request
from datetime import datetime, timezone

import bcrypt
import psycopg2
from psycopg2 import sql, Error as PGError
from flask import Flask, request, jsonify, send_from_directory, session
from flask_cors import CORS

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# static_folder is disabled on purpose: Flask's built-in static rule is also
# "/<path:filename>", and it would shadow the static_files() catch-all below — which
# is what gates /eventos.html and falls back to public/. All file serving goes
# through static_files() so those rules cannot be bypassed.
app = Flask(__name__, static_folder=None)
SITE_ROOT = os.path.dirname(os.path.abspath(__file__))
PUBLIC_ROOT = os.path.join(SITE_ROOT, 'public')
CORS(app)

# The session cookie authenticates the staff panel, so a predictable key would let
# anyone forge an admin session. Fall back to a random per-process key instead of a
# hardcoded one; sessions then reset on restart until FLASK_SECRET_KEY is provided.
_secret_key = os.environ.get('FLASK_SECRET_KEY')
if not _secret_key:
    _secret_key = secrets.token_hex(32)
    logger.warning(
        'FLASK_SECRET_KEY is not set: generated a temporary key. Logins will be '
        'dropped whenever the site restarts. Set FLASK_SECRET_KEY in production.'
    )
app.secret_key = _secret_key

# Database configuration from environment
DB_CONFIG = {
    'host': os.environ.get('DB_HOST', 'database'),
    'port': int(os.environ.get('DB_PORT', '5432')),
    'database': os.environ.get('DB_NAME', 'openmu'),
    'user': os.environ.get('DB_USER', 'postgres'),
    'password': os.environ.get('DB_PASSWORD', 'postgres'),
}


def get_db_connection():
    """Create and return a database connection."""
    try:
        conn = psycopg2.connect(**DB_CONFIG)
        conn.autocommit = False
        return conn
    except PGError as e:
        logger.error(f"Database connection failed: {e}")
        raise


def hash_password(password: str) -> str:
    """Hash password using BCrypt (compatible with OpenMU)."""
    salt = bcrypt.gensalt(rounds=12)
    hashed = bcrypt.hashpw(password.encode('utf-8'), salt)
    return hashed.decode('utf-8')


def validate_login_name(login_name: str) -> bool:
    """Validate login name: 3-10 alphanumeric characters."""
    return bool(re.match(r'^[a-zA-Z0-9]{3,10}$', login_name))


def validate_email(email: str) -> bool:
    """Basic email validation."""
    return bool(re.match(r'^[^\s@]+@[^\s@]+\.[^\s@]+$', email))


def check_account_exists(cursor, login_name: str) -> bool:
    """Check if an account with the given login name already exists."""
    cursor.execute(
        'SELECT 1 FROM "data"."Account" WHERE "LoginName" = %s LIMIT 1',
        (login_name,)
    )
    return cursor.fetchone() is not None


def check_email_exists(cursor, email: str) -> bool:
    """Check if an account with the given email already exists."""
    cursor.execute(
        'SELECT 1 FROM "data"."Account" WHERE "EMail" = %s LIMIT 1',
        (email,)
    )
    return cursor.fetchone() is not None


def create_account(login_name: str, password: str, email: str, security_code: str) -> dict:
    """
    Create a new account in the OpenMU database.
    
    AccountState: 0 = Normal, 1 = Spectator, 2 = GameMaster, 
                  3 = GameMasterInvisible, 4 = Banned, 5 = TemporarilyBanned
    """
    conn = get_db_connection()
    try:
        cursor = conn.cursor()

        # Check for duplicates
        if check_account_exists(cursor, login_name):
            raise ValueError('Login já está em uso')

        if check_email_exists(cursor, email):
            raise ValueError('E-mail já está cadastrado')

        # Hash password and security code with BCrypt
        password_hash = hash_password(password)
        security_code_hash = hash_password(security_code)

        # Generate UUID for account
        account_id = str(uuid.uuid4())

        # Insert account with State = 0 (Normal)
        cursor.execute(
            '''
            INSERT INTO "data"."Account" (
                "Id", "LoginName", "PasswordHash", "SecurityCode",
                "EMail", "LanguageIsoCode", "RegistrationDate",
                "State", "TimeZone", "VaultPassword", "IsVaultExtended", "IsTemplate"
            ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
            ''',
            (
                account_id,
                login_name,
                password_hash,
                security_code_hash,
                email,
                'pt',  # Default language: Portuguese
                datetime.now(timezone.utc),
                0,     # AccountState.Normal
                0,     # TimeZone UTC
                '',    # VaultPassword (empty by default)
                False, # IsVaultExtended
                False  # IsTemplate
            )
        )

        conn.commit()
        logger.info(f"Account created successfully: {login_name} (ID: {account_id})")

        return {
            'id': account_id,
            'loginName': login_name,
            'email': email,
            'registrationDate': datetime.now(timezone.utc).isoformat()
        }

    except ValueError:
        conn.rollback()
        raise
    except PGError as e:
        conn.rollback()
        logger.error(f"Database error creating account: {e}")
        raise RuntimeError('Erro interno ao criar conta')
    finally:
        conn.close()


def get_account_by_login(login_name: str):
    """Find an account by its login name."""
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        cursor.execute(
            'SELECT "Id", "LoginName", "PasswordHash", "EMail", "State" '
            'FROM "data"."Account" WHERE "LoginName" = %s LIMIT 1',
            (login_name,)
        )
        return cursor.fetchone()
    finally:
        conn.close()


@app.route('/')
def index():
    """Serve the main website."""
    return send_from_directory(SITE_ROOT, 'index.html')


# ---------------------------------------------------------------------------
# Staff events panel (eventos.html)
# ---------------------------------------------------------------------------

# AccountState values that may edit the events panel: 2 = GameMaster,
# 3 = GameMasterInvisible.
ADMIN_ACCOUNT_STATES = (2, 3)

# Key under which the events panel configuration is stored.
EVENTS_CONFIG_KEY = 'server_events'

# Fields of a single event, kept in sync with public/js/eventos.js and with the
# event cards of the home page.
EVENT_TEXT_FIELDS = ('id', 'name', 'category', 'icon', 'colorTheme', 'location',
                     'frequency', 'startTimeStr', 'rewardTag', 'description')
EVENT_INT_FIELDS = ('startIntervalMin', 'startOffsetMin', 'durationMin')


def get_session_account():
    """Return (loginName, state) of the signed-in account, or None."""
    account_id = session.get('account_id')
    if not account_id:
        return None

    try:
        conn = get_db_connection()
        try:
            cursor = conn.cursor()
            cursor.execute(
                'SELECT "LoginName", "State" FROM "data"."Account" WHERE "Id" = %s LIMIT 1',
                (account_id,)
            )
            return cursor.fetchone()
        finally:
            conn.close()
    except PGError as error:
        logger.error(f"Could not read the signed-in account: {error}")
        return None


def is_admin_session() -> bool:
    """Check whether the current session belongs to a game master."""
    account = get_session_account()
    return bool(account) and account[1] in ADMIN_ACCOUNT_STATES


def ensure_site_config_table(cursor):
    """Create the website's own configuration table if it does not exist yet.

    It lives in a separate "website" schema so it never collides with the schemas
    that OpenMU manages through its own migrations. The database is used instead of
    a file because the website container has no persistent volume.
    """
    cursor.execute('CREATE SCHEMA IF NOT EXISTS "website"')
    cursor.execute(
        'CREATE TABLE IF NOT EXISTS "website"."SiteConfig" ('
        '"Key" text PRIMARY KEY, '
        '"Value" text NOT NULL, '
        '"UpdatedAt" timestamptz NOT NULL DEFAULT now(), '
        '"UpdatedBy" text)'
    )


def read_events_config():
    """Return the stored events panel configuration, or None when unset."""
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        ensure_site_config_table(cursor)
        conn.commit()
        cursor.execute(
            'SELECT "Value" FROM "website"."SiteConfig" WHERE "Key" = %s',
            (EVENTS_CONFIG_KEY,)
        )
        row = cursor.fetchone()
        if not row:
            return None

        events = json.loads(row[0])
        return events if isinstance(events, list) and events else None
    except (ValueError, TypeError) as error:
        logger.error(f"Stored events configuration is not valid JSON: {error}")
        return None
    finally:
        conn.close()


def write_events_config(events, updated_by: str):
    """Persist the events panel configuration."""
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        ensure_site_config_table(cursor)
        cursor.execute(
            'INSERT INTO "website"."SiteConfig" ("Key", "Value", "UpdatedAt", "UpdatedBy") '
            'VALUES (%s, %s, %s, %s) '
            'ON CONFLICT ("Key") DO UPDATE SET '
            '"Value" = EXCLUDED."Value", "UpdatedAt" = EXCLUDED."UpdatedAt", '
            '"UpdatedBy" = EXCLUDED."UpdatedBy"',
            (EVENTS_CONFIG_KEY, json.dumps(events), datetime.now(timezone.utc), updated_by)
        )
        conn.commit()
    except PGError:
        conn.rollback()
        raise
    finally:
        conn.close()


def delete_events_config():
    """Drop the stored configuration so the site falls back to the game data."""
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        ensure_site_config_table(cursor)
        cursor.execute('DELETE FROM "website"."SiteConfig" WHERE "Key" = %s', (EVENTS_CONFIG_KEY,))
        conn.commit()
    except PGError:
        conn.rollback()
        raise
    finally:
        conn.close()


def sanitize_events(raw_events):
    """Validate and normalize the event list coming from the panel.

    Only known fields are kept, so the panel can never store arbitrary data that
    later gets rendered on the public home page.
    """
    if not isinstance(raw_events, list) or not raw_events:
        raise ValueError('Envie ao menos um evento')

    if len(raw_events) > 60:
        raise ValueError('Limite de 60 eventos excedido')

    events = []
    seen_ids = set()
    for raw in raw_events:
        if not isinstance(raw, dict):
            raise ValueError('Formato de evento inválido')

        name = str(raw.get('name', '')).strip()
        if not name:
            raise ValueError('Todo evento precisa de um nome')

        event = {}
        for field in EVENT_TEXT_FIELDS:
            value = raw.get(field)
            if value is not None:
                event[field] = str(value)[:300]

        event['name'] = name[:120]
        event['category'] = 'invasions' if event.get('category') == 'invasions' else 'events'

        event_id = (event.get('id') or '').strip() or f'evt-{uuid.uuid4().hex[:8]}'
        if event_id in seen_ids:
            raise ValueError(f'Evento duplicado: {event_id}')
        seen_ids.add(event_id)
        event['id'] = event_id[:60]

        for field in EVENT_INT_FIELDS:
            try:
                event[field] = int(float(raw.get(field, 0) or 0))
            except (TypeError, ValueError):
                raise ValueError(f'Valor numérico inválido em "{field}"')

        # Keep the schedule maths safe for the countdown on the home page.
        event['startIntervalMin'] = min(max(event['startIntervalMin'] or 120, 1), 10080)
        event['startOffsetMin'] = min(max(event['startOffsetMin'], 0), 10079)
        event['durationMin'] = min(max(event['durationMin'] or 1, 1), event['startIntervalMin'])
        event['enabled'] = raw.get('enabled') is not False
        events.append(event)

    return events


@app.route('/eventos.html')
def events_panel():
    """Serve the staff events panel, but only to signed-in game masters."""
    if not is_admin_session():
        return send_from_directory(SITE_ROOT, 'eventos-login.html'), 401
    return send_from_directory(PUBLIC_ROOT, 'eventos.html')


@app.route('/api/events/config', methods=['GET'])
def get_events_config():
    """Return the stored panel configuration (game masters only)."""
    if not is_admin_session():
        return jsonify({'success': False, 'message': 'Acesso restrito à equipe'}), 403

    try:
        return jsonify({'success': True, 'events': read_events_config() or []}), 200
    except Exception as error:
        logger.error(f"Error reading events configuration: {error}")
        return jsonify({'success': False, 'message': 'Erro ao carregar configuração'}), 500


@app.route('/api/events/config', methods=['PUT', 'POST'])
def save_events_config():
    """Store the panel configuration so the home page reflects it."""
    account = get_session_account()
    if not account or account[1] not in ADMIN_ACCOUNT_STATES:
        return jsonify({'success': False, 'message': 'Acesso restrito à equipe'}), 403

    try:
        payload = request.get_json(silent=True) or {}
        events = sanitize_events(payload.get('events'))
        write_events_config(events, account[0])
        return jsonify({'success': True, 'events': events}), 200
    except ValueError as error:
        return jsonify({'success': False, 'message': str(error)}), 400
    except Exception as error:
        logger.error(f"Error saving events configuration: {error}")
        return jsonify({'success': False, 'message': 'Erro ao salvar configuração'}), 500


@app.route('/api/events/config', methods=['DELETE'])
def reset_events_config():
    """Remove the stored configuration and fall back to the game's own data."""
    if not is_admin_session():
        return jsonify({'success': False, 'message': 'Acesso restrito à equipe'}), 403

    try:
        delete_events_config()
        return jsonify({'success': True}), 200
    except Exception as error:
        logger.error(f"Error resetting events configuration: {error}")
        return jsonify({'success': False, 'message': 'Erro ao restaurar configuração'}), 500


@app.route('/api/session', methods=['GET'])
def get_session_info():
    """Report whether the visitor is signed in and may open the staff panel."""
    account = get_session_account()
    return jsonify({
        'success': True,
        'loggedIn': bool(account),
        'loginName': account[0] if account else None,
        'isAdmin': bool(account) and account[1] in ADMIN_ACCOUNT_STATES
    }), 200


@app.route('/<path:path>')
def static_files(path):
    """Serve static files.

    Files placed in "public" are also served from the root, mirroring what the
    Angular build does with that folder, so the panel's assets keep working
    without having to duplicate them.
    """
    if '..' in path:
        return jsonify({'success': False, 'message': 'Caminho inválido'}), 400

    # The panel itself must always go through the authenticated route above.
    if path.lower() in ('eventos.html', 'public/eventos.html'):
        return events_panel()

    if os.path.isfile(os.path.join(SITE_ROOT, path)):
        return send_from_directory(SITE_ROOT, path)

    if os.path.isfile(os.path.join(PUBLIC_ROOT, path)):
        return send_from_directory(PUBLIC_ROOT, path)

    return send_from_directory(SITE_ROOT, path)


@app.route('/api/register', methods=['POST'])
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

        # Server-side validation
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

        # Create account
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


@app.route('/api/login', methods=['POST'])
def login():
    """Authenticate an account and start a browser session."""
    try:
        data = request.get_json() or {}
        login_name = data.get('loginName', '').strip()
        password = data.get('password', '')

        if not login_name or not password:
            return jsonify({'success': False, 'message': 'Informe login e senha'}), 400

        account = get_account_by_login(login_name)
        if not account or not bcrypt.checkpw(password.encode('utf-8'), account[2].encode('utf-8')):
            return jsonify({'success': False, 'message': 'Login ou senha inválidos'}), 401

        if account[4] in (4, 5):
            return jsonify({'success': False, 'message': 'Esta conta está bloqueada'}), 403

        session.clear()
        session['account_id'] = str(account[0])
        return jsonify({'success': True, 'loginName': account[1]}), 200
    except Exception as e:
        logger.error(f"Login error: {e}")
        return jsonify({'success': False, 'message': 'Erro interno do servidor'}), 500


@app.route('/api/logout', methods=['POST'])
def logout():
    session.clear()
    return jsonify({'success': True}), 200


@app.route('/api/me', methods=['GET'])
def account_dashboard():
    """Return the signed-in account and all of its character statistics."""
    account_id = session.get('account_id')
    if not account_id:
        return jsonify({'success': False, 'message': 'Faça login para continuar'}), 401

    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        cursor.execute('''
            SELECT
                a."LoginName",
                a."EMail",
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
        conn.close()

        if not rows:
            return jsonify({'success': False, 'message': 'Conta não encontrada'}), 404

        online_names = set()
        try:
            with urllib.request.urlopen('http://openmu:8080/api/status', timeout=3) as response:
                online_names = set(json.load(response).get('playersList', []))
        except (OSError, ValueError, TypeError) as error:
            logger.warning(f"Could not read online character list: {error}")

        characters = []
        for row in rows:
            if not row[3]:
                continue
            characters.append({
                'id': str(row[2]),
                'name': row[3],
                'className': row[4],
                'classCode': row[4],
                'guild': row[14],
                'level': calculate_level_from_experience(row[5] or 0),
                'masterLevel': calculate_level_from_experience(row[6] or 0),
                'kills': int(row[7] or 0),
                'resets': int(row[8] or 0),
                'masterResets': int(row[9] or 0),
                'strength': int(row[10] or 0),
                'agility': int(row[11] or 0),
                'vitality': int(row[12] or 0),
                'energy': int(row[13] or 0),
                'status': 'Online' if row[3] in online_names else 'Offline'
            })

        return jsonify({'success': True, 'account': {
            'loginName': rows[0][0],
            'email': rows[0][1],
            'characters': characters
        }}), 200
    except Exception as e:
        logger.error(f"Error getting account dashboard: {e}")
        return jsonify({'success': False, 'message': 'Erro ao carregar painel da conta'}), 500


@app.route('/api/health', methods=['GET'])
def health_check():
    """Health check endpoint."""
    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        cursor.execute('SELECT 1')
        cursor.fetchone()
        conn.close()
        return jsonify({'status': 'healthy', 'database': 'connected'}), 200
    except Exception as e:
        logger.error(f"Health check failed: {e}")
        return jsonify({'status': 'unhealthy', 'database': 'disconnected'}), 503


@app.route('/api/ranking', methods=['GET'])
def get_ranking():
    """Get top 5 characters by level (experience)."""
    try:
        conn = get_db_connection()
        cursor = conn.cursor()

        # Get top 5 characters by experience
        # Level is calculated from experience using OpenMU's formula
        cursor.execute('''
            SELECT 
                c."Name" as character_name,
                COALESCE(g."Name", '-') as guild_name,
                c."Experience" as experience,
                COALESCE(cc."Name", '-') as class_name,
                COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                    JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                    WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Resets' LIMIT 1), 0) as resets
            FROM "data"."Character" c
            LEFT JOIN "config"."CharacterClass" cc ON cc."Id" = c."CharacterClassId"
            LEFT JOIN "guild"."GuildMember" gm ON c."Id" = gm."Id"
            LEFT JOIN "guild"."Guild" g ON gm."GuildId" = g."Id"
            WHERE c."CharacterStatus" = 0
            ORDER BY c."Experience" DESC
            LIMIT 5
        ''')

        rows = cursor.fetchall()
        conn.close()

        ranking = []
        for i, row in enumerate(rows, 1):
            experience = row[2] or 0
            # Calculate level from experience using OpenMU formula
            level = calculate_level_from_experience(experience)
            
            ranking.append({
                'position': i,
                'characterName': row[0],
                'guildName': row[1] if row[1] else '-',
                'level': level,
                'className': row[3],
                'resets': int(row[4] or 0)
            })

        return jsonify({
            'success': True,
            'ranking': ranking
        }), 200

    except Exception as e:
        logger.error(f"Error getting ranking: {e}")
        return jsonify({
            'success': False,
            'message': 'Erro ao carregar ranking',
            'ranking': []
        }), 500


@app.route('/api/ranking/deaths', methods=['GET'])
def get_deaths_ranking():
    """Get top 5 characters by death count (PlayerKillCount)."""
    try:
        conn = get_db_connection()
        cursor = conn.cursor()

        # Get top 5 characters by PlayerKillCount (deaths)
        cursor.execute('''
            SELECT 
                c."Name" as character_name,
                COALESCE(g."Name", '-') as guild_name,
                c."PlayerKillCount" as kill_count,
                COALESCE(cc."Name", '-') as class_name
            FROM "data"."Character" c
            LEFT JOIN "config"."CharacterClass" cc ON cc."Id" = c."CharacterClassId"
            LEFT JOIN "guild"."GuildMember" gm ON c."Id" = gm."Id"
            LEFT JOIN "guild"."Guild" g ON gm."GuildId" = g."Id"
            WHERE c."CharacterStatus" = 0
            ORDER BY c."PlayerKillCount" DESC
            LIMIT 5
        ''')

        rows = cursor.fetchall()
        conn.close()

        ranking = []
        for i, row in enumerate(rows, 1):
            ranking.append({
                'position': i,
                'characterName': row[0],
                'guildName': row[1] if row[1] else '-',
                'kills': int(row[2]) if row[2] else 0,
                'className': row[3]
            })

        return jsonify({
            'success': True,
            'ranking': ranking
        }), 200

    except Exception as e:
        logger.error(f"Error getting deaths ranking: {e}")
        return jsonify({
            'success': False,
            'message': 'Erro ao carregar ranking de mortes',
            'ranking': []
        }), 500


def get_minigame_ranking(name_pattern):
    """Return persisted ranking entries for configured mini-games."""
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        cursor.execute('''
            SELECT c."Name", COALESCE(g."Name", '-'), COALESCE(cc."Name", '-'),
                   MAX(e."Score") AS score
            FROM "data"."MiniGameRankingEntry" e
            JOIN "config"."MiniGameDefinition" md ON md."Id" = e."MiniGameId"
            JOIN "data"."Character" c ON c."Id" = e."CharacterId"
            LEFT JOIN "config"."CharacterClass" cc ON cc."Id" = c."CharacterClassId"
            LEFT JOIN "guild"."GuildMember" gm ON gm."Id" = c."Id"
            LEFT JOIN "guild"."Guild" g ON g."Id" = gm."GuildId"
            WHERE md."Name" ILIKE %s
            GROUP BY c."Name", g."Name", cc."Name"
            ORDER BY score DESC
            LIMIT 5
        ''', (name_pattern,))
        return [{
            'position': index,
            'characterName': row[0],
            'guildName': row[1],
            'className': row[2],
            'score': int(row[3] or 0),
            'extraStat': f'{int(row[3] or 0)} pontos',
            'subStat': 'Ranking persistido do servidor'
        } for index, row in enumerate(cursor.fetchall(), 1)]
    finally:
        conn.close()


@app.route('/api/ranking/resets', methods=['GET'])
def get_resets_ranking():
    """Get top characters by the persisted Resets attribute."""
    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        cursor.execute('''
            SELECT c."Name", COALESCE(g."Name", '-'), COALESCE(cc."Name", '-'),
                   COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
                     JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
                     WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Resets' LIMIT 1), 0)
            FROM "data"."Character" c
            LEFT JOIN "config"."CharacterClass" cc ON cc."Id" = c."CharacterClassId"
            LEFT JOIN "guild"."GuildMember" gm ON gm."Id" = c."Id"
            LEFT JOIN "guild"."Guild" g ON g."Id" = gm."GuildId"
            WHERE c."CharacterStatus" = 0
            ORDER BY 4 DESC, c."Name"
            LIMIT 5
        ''')
        return jsonify({'success': True, 'ranking': [{
            'position': index, 'characterName': row[0], 'guildName': row[1],
            'className': row[2], 'resets': int(row[3] or 0)
        } for index, row in enumerate(cursor.fetchall(), 1)]}), 200
    except Exception as error:
        logger.error(f"Error getting reset ranking: {error}")
        return jsonify({'success': False, 'ranking': []}), 500


@app.route('/api/ranking/events/<event_name>', methods=['GET'])
def get_event_ranking(event_name):
    patterns = {'blood-castle': '%Blood Castle%', 'devil-square': '%Devil Square%'}
    if event_name not in patterns:
        return jsonify({'success': False, 'message': 'Evento inválido', 'ranking': []}), 404
    try:
        return jsonify({'success': True, 'ranking': get_minigame_ranking(patterns[event_name])}), 200
    except Exception as error:
        logger.error(f"Error getting {event_name} ranking: {error}")
        return jsonify({'success': False, 'ranking': []}), 500


# Type ids (GUIDs) of the periodic "start" plugins that automatically open each
# mini-game. They come from the GameLogic source (MiniGameStartBasePlugIn
# implementations). When such a plugin is active the event runs on a fixed
# timetable, so the website should show a live countdown instead of waiting for a
# manual trigger.
EVENT_START_PLUGIN_IDS = {
    'blood-castle': '95e68c14-ad87-4b3c-af46-45b8f1c3bc2a',
    'devil-square': '61c61a58-211e-4d6a-9ea1-d25e0c4a47c5',
    'chaos-castle': '3ad96a70-ed24-4979-80b8-169e461e548f',
}


@app.route('/api/events/schedule', methods=['GET'])
def get_events_schedule():
    """Return the event cards shown in the "Eventos do Servidor" panel.

    When the staff panel (eventos.html) has a stored configuration, it is
    authoritative: the response carries the full list and "replace": true, so the
    home page renders exactly what the panel defines. Otherwise the mini-game
    definitions from the game database are returned as a partial overlay
    ("replace": false), which only adjusts the site's built-in cards.

    Blood Castle, Devil Square and Chaos Castle are opened by periodic start
    plugins. While the start plugin is active the event is on an automatic
    timetable ('fixed'), so the front-end renders a countdown. Only a disabled
    start plugin makes the event effectively manual.
    """
    try:
        stored_events = read_events_config()
    except Exception as error:
        logger.error(f"Error reading stored events configuration: {error}")
        stored_events = None

    if stored_events:
        events = [{
            'id': event.get('id'),
            'name': event.get('name'),
            'icon': event.get('icon') or '⚔️',
            'category': 'invasions' if event.get('category') == 'invasions' else 'events',
            'location': event.get('location') or '-',
            'frequency': event.get('frequency') or '-',
            'rewardTag': event.get('rewardTag') or '-',
            'colorTheme': event.get('colorTheme') or '#d4af37',
            'startIntervalMin': event.get('startIntervalMin') or 120,
            'startOffsetMin': event.get('startOffsetMin') or 0,
            'durationMin': event.get('durationMin') or 15,
            'scheduleMode': 'fixed',
        } for event in stored_events if event.get('enabled') is not False]
        return jsonify({'success': True, 'replace': True, 'events': events}), 200

    try:
        conn = get_db_connection()
        cursor = conn.cursor()
        cursor.execute('''
            SELECT CASE
                     WHEN "Name" ILIKE 'Blood Castle%%' THEN 'blood-castle'
                     WHEN "Name" ILIKE 'Devil Square%%' THEN 'devil-square'
                     WHEN "Name" ILIKE 'Chaos Castle%%' THEN 'chaos-castle'
                   END AS event_id,
                   MIN("Name"), MIN(EXTRACT(EPOCH FROM "EnterDuration")),
                   MIN(EXTRACT(EPOCH FROM "GameDuration"))
            FROM "config"."MiniGameDefinition"
            WHERE "Name" ILIKE 'Blood Castle%%'
               OR "Name" ILIKE 'Devil Square%%'
               OR "Name" ILIKE 'Chaos Castle%%'
            GROUP BY 1
            ORDER BY 1
        ''')
        rows = cursor.fetchall()

        # Find out which start plugins are enabled so we can tell an automatically
        # scheduled event from a genuinely manual one. Missing rows default to
        # active, because these plugins are active by default.
        active_by_event = {event_id: True for event_id in EVENT_START_PLUGIN_IDS}
        try:
            cursor.execute(
                'SELECT "TypeId"::text, "IsActive" FROM "config"."PlugInConfiguration" '
                'WHERE "TypeId"::text IN %s',
                (tuple(EVENT_START_PLUGIN_IDS.values()),)
            )
            active_by_type = {type_id: is_active for type_id, is_active in cursor.fetchall()}
            for event_id, type_id in EVENT_START_PLUGIN_IDS.items():
                active_by_event[event_id] = active_by_type.get(type_id, True)
        except PGError as plugin_error:
            logger.warning(f"Could not read event start plugin state: {plugin_error}")

        conn.close()

        events = []
        for event_id, name, enter_seconds, game_seconds in rows:
            if not event_id:
                continue
            # Only send the fields the front-end should override. The website keeps
            # its own countdown schedule (start offset/interval and frequency text),
            # matching the other automatic events (Golden Invasion, Illusion Temple).
            event = {
                'id': event_id,
                'name': name.rsplit(' ', 1)[0],
                'scheduleMode': 'fixed' if active_by_event.get(event_id, True) else 'manual',
                'enterDurationMin': int((enter_seconds or 0) / 60),
            }
            duration_min = int((game_seconds or enter_seconds or 0) / 60)
            if duration_min > 0:
                event['durationMin'] = duration_min
            events.append(event)
        return jsonify({'success': True, 'replace': False, 'events': events}), 200
    except Exception as error:
        logger.error(f"Error getting event schedule: {error}")
        return jsonify({'success': False, 'events': []}), 500


@app.route('/api/server-info', methods=['GET'])
def server_info():
    """Get server information for the website."""
    try:
        conn = get_db_connection()
        cursor = conn.cursor()

        # Count total accounts
        cursor.execute('SELECT COUNT(*) FROM "data"."Account"')
        total_accounts = cursor.fetchone()[0]

        conn.close()

        online_players = 0
        try:
            with urllib.request.urlopen('http://openmu:8080/api/status', timeout=3) as response:
                status = json.load(response)
                online_players = int(status.get('players', 0))
        except (OSError, ValueError, TypeError) as error:
            logger.warning(f"Could not read OpenMU online player count: {error}")

        return jsonify({
            'serverName': 'MU Online',
            'season': 'Ancient',
            'version': '2.0',
            'totalAccounts': total_accounts,
            'onlinePlayers': online_players,
            'status': 'online'
        }), 200

    except Exception as e:
        logger.error(f"Error getting server info: {e}")
        return jsonify({
            'serverName': 'MU Online',
            'season': 'Ancient',
            'version': '2.0',
            'status': 'online'
        }), 200


def calculate_level_from_experience(experience):
    """
    Calculate character level from experience points.
    Uses OpenMU's experience formula reversed.
    Formula: XP = 10 * (level + 8) * (level - 1)^2 for level < 256
    """
    if experience <= 0:
        return 1
    
    # For levels 1-255: XP = 10 * (L + 8) * (L - 1)^2
    # Approximate inverse: L ≈ (XP / 10)^(1/3) + 1
    # More precise: solve cubic equation
    
    # Binary search for level (more reliable than formula)
    low, high = 1, 400
    while low < high:
        mid = (low + high + 1) // 2
        # Calculate XP needed for this level
        if mid < 256:
            xp_needed = 10 * (mid + 8) * (mid - 1) * (mid - 1)
        else:
            base_xp = 10 * (255 + 8) * (255 - 1) * (255 - 1)
            xp_needed = base_xp + (1000 * (mid - 247) * (mid - 256) * (mid - 256))
        
        if xp_needed <= experience:
            low = mid
        else:
            high = mid - 1
    
    return low


if __name__ == '__main__':
    port = int(os.environ.get('PORT', 5000))
    debug = os.environ.get('FLASK_DEBUG', 'false').lower() == 'true'
    app.run(host='0.0.0.0', port=port, debug=debug)
