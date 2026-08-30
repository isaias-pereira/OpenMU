"""Account creation logic (registration)."""
import logging
import uuid
from datetime import datetime, timezone

from psycopg2 import Error as PGError

from . import db
from .security import hash_password

logger = logging.getLogger(__name__)


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
    conn = db.get_db_connection()
    try:
        cursor = conn.cursor()

        if check_account_exists(cursor, login_name):
            raise ValueError('Login já está em uso')

        if check_email_exists(cursor, email):
            raise ValueError('E-mail já está cadastrado')

        password_hash = hash_password(password)
        security_code_hash = hash_password(security_code)
        account_id = str(uuid.uuid4())

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
