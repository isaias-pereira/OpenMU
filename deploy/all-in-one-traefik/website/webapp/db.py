"""Database access helpers.

Routes call ``db.get_db_connection()`` through the module (not a direct import)
so tests can monkeypatch the connection factory.
"""
import logging

import psycopg2
from psycopg2 import Error as PGError

from .config import DB_CONFIG

logger = logging.getLogger(__name__)


def get_db_connection():
    """Create and return a database connection."""
    try:
        conn = psycopg2.connect(**DB_CONFIG)
        conn.autocommit = False
        return conn
    except PGError as e:
        logger.error(f"Database connection failed: {e}")
        raise
