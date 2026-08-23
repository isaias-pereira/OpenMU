import os
import re
import uuid
import math
import logging
import json
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

app = Flask(__name__, static_folder='.', static_url_path='')
CORS(app)
app.secret_key = os.environ.get('FLASK_SECRET_KEY', 'change-this-secret-key')

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
    return send_from_directory('.', 'index.html')


@app.route('/<path:path>')
def static_files(path):
    """Serve static files."""
    return send_from_directory('.', path)


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


@app.route('/api/events/schedule', methods=['GET'])
def get_events_schedule():
    """Return event definitions and whether the plugin has an automatic schedule."""
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
        events = []
        for event_id, name, enter_seconds, game_seconds in cursor.fetchall():
            events.append({
                'id': event_id,
                'name': name.rsplit(' ', 1)[0],
                'frequency': 'Manual pelo plugin',
                'scheduleMode': 'manual',
                'enterDurationMin': int((enter_seconds or 0) / 60),
                'durationMin': int((game_seconds or 0) / 60)
            })
        return jsonify({'success': True, 'events': events}), 200
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
