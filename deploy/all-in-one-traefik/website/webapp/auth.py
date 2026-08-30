"""Session helpers and account lookups shared by the routes."""
import logging

from flask import session
from psycopg2 import Error as PGError

from . import db
from .config import ADMIN_ACCOUNT_STATES

logger = logging.getLogger(__name__)


def get_session_account():
    """Return (loginName, state) of the signed-in account, or None."""
    account_id = session.get('account_id')
    if not account_id:
        return None

    try:
        conn = db.get_db_connection()
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


def get_account_by_login(login_name: str):
    """Find an account by its login name."""
    conn = db.get_db_connection()
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
