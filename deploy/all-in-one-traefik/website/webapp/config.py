"""Centralized configuration for the website service.

Everything environment-driven lives here so the rest of the code never touches
os.environ directly.
"""
import logging
import os
import secrets

logger = logging.getLogger(__name__)

SITE_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PUBLIC_ROOT = os.path.join(SITE_ROOT, 'public')

# Database configuration from environment
DB_CONFIG = {
    'host': os.environ.get('DB_HOST', 'database'),
    'port': int(os.environ.get('DB_PORT', '5432')),
    'database': os.environ.get('DB_NAME', 'openmu'),
    'user': os.environ.get('DB_USER', 'postgres'),
    'password': os.environ.get('DB_PASSWORD', 'postgres'),
}

# Base URL of the OpenMU admin API (used for online player count / list).
# Overridable so local development can point at a different host/port.
OPENMU_API_URL = os.environ.get('OPENMU_API_URL', 'http://openmu:8080')

# AccountState values that may edit the events panel: 2 = GameMaster,
# 3 = GameMasterInvisible.
ADMIN_ACCOUNT_STATES = (2, 3)

# AccountState values that block login: 4 = Banned, 5 = TemporarilyBanned.
BANNED_ACCOUNT_STATES = (4, 5)

# Preço de cada plano VIP, em WCoin. A tabela data."VipPlan" não guarda preço — ele vive
# na config do plugin do jogo (VipChatCommandPlugIn.VipCommandConfiguration). Espelhamos os
# mesmos valores aqui para a compra pelo site; overridável por ambiente (VIP_PRICE_<PLANO>)
# para ajustar sem rebuild. Mantenha em sincronia com o plugin do jogo.
VIP_PRICES = {
    'Bronze': int(os.environ.get('VIP_PRICE_BRONZE', '1000')),
    'Silver': int(os.environ.get('VIP_PRICE_SILVER', '2500')),
    'Gold': int(os.environ.get('VIP_PRICE_GOLD', '5000')),
    'Platinum': int(os.environ.get('VIP_PRICE_PLATINUM', '10000')),
}

# Valor do enum WCoinTransactionType.Purchase (Exchange=0, Purchase=1, AdminAdjust=2),
# gravado na coluna inteira data."WCoinTransaction"."Type".
WCOIN_TX_PURCHASE = 1


def resolve_secret_key() -> str:
    """Return the Flask secret key.

    The session cookie authenticates the staff panel, so a predictable key would
    let anyone forge an admin session. Fall back to a random per-process key
    instead of a hardcoded one; sessions then reset on restart until
    FLASK_SECRET_KEY is provided.
    """
    secret_key = os.environ.get('FLASK_SECRET_KEY')
    if not secret_key:
        secret_key = secrets.token_hex(32)
        logger.warning(
            'FLASK_SECRET_KEY is not set: generated a temporary key. Logins will '
            'be dropped whenever the site restarts. Set FLASK_SECRET_KEY in '
            'production.'
        )
    return secret_key
