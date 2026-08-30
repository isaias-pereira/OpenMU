"""Client for the OpenMU admin API (online players, server status)."""
import json
import logging
import urllib.request

from .config import OPENMU_API_URL

logger = logging.getLogger(__name__)

STATUS_TIMEOUT_SECONDS = 3


def get_server_status() -> dict:
    """Fetch the OpenMU status payload, or an empty dict when unreachable."""
    url = f'{OPENMU_API_URL}/api/status'
    try:
        with urllib.request.urlopen(url, timeout=STATUS_TIMEOUT_SECONDS) as response:
            return json.load(response)
    except (OSError, ValueError, TypeError) as error:
        logger.warning(f"Could not read OpenMU status from {url}: {error}")
        return {}


def get_online_player_count() -> int:
    """Return how many players are online (0 when the API is unreachable)."""
    try:
        return int(get_server_status().get('players', 0))
    except (ValueError, TypeError):
        return 0


def get_online_character_names() -> set:
    """Return the set of online character names (empty when unreachable)."""
    return set(get_server_status().get('playersList', []))
