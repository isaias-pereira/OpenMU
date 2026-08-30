"""Health check and public server information."""
import logging

from flask import Blueprint, jsonify

from .. import db
from ..openmu import get_online_player_count

logger = logging.getLogger(__name__)

bp = Blueprint('misc', __name__)

SERVER_INFO_FALLBACK = {
    'serverName': 'MU Online',
    'season': 'Ancient',
    'version': '2.0',
    'status': 'online',
}


@bp.route('/api/health', methods=['GET'])
def health_check():
    """Health check endpoint."""
    try:
        conn = db.get_db_connection()
        try:
            cursor = conn.cursor()
            cursor.execute('SELECT 1')
            cursor.fetchone()
        finally:
            conn.close()
        return jsonify({'status': 'healthy', 'database': 'connected'}), 200
    except Exception as e:
        logger.error(f"Health check failed: {e}")
        return jsonify({'status': 'unhealthy', 'database': 'disconnected'}), 503


@bp.route('/api/server-info', methods=['GET'])
def server_info():
    """Get server information for the website."""
    try:
        conn = db.get_db_connection()
        try:
            cursor = conn.cursor()
            cursor.execute('SELECT COUNT(*) FROM "data"."Account"')
            total_accounts = cursor.fetchone()[0]
        finally:
            conn.close()

        return jsonify({
            **SERVER_INFO_FALLBACK,
            'totalAccounts': total_accounts,
            'onlinePlayers': get_online_player_count(),
        }), 200

    except Exception as e:
        logger.error(f"Error getting server info: {e}")
        return jsonify(SERVER_INFO_FALLBACK), 200
