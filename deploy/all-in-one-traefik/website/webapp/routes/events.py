"""Events panel configuration API and the public events schedule."""
import logging

from flask import Blueprint, jsonify, request
from psycopg2 import Error as PGError

from .. import db
from ..auth import get_session_account, is_admin_session
from ..config import ADMIN_ACCOUNT_STATES
from ..events_store import (
    delete_events_config,
    read_events_config,
    sanitize_events,
    write_events_config,
)

logger = logging.getLogger(__name__)

bp = Blueprint('events', __name__)

# Type ids (GUIDs) of the periodic "start" plugins that automatically open each
# mini-game. They come from the GameLogic source (MiniGameStartBasePlugIn
# implementations). When such a plugin is active the event runs on a fixed
# timetable, so the website should show a live countdown instead of waiting for
# a manual trigger.
EVENT_START_PLUGIN_IDS = {
    'blood-castle': '95e68c14-ad87-4b3c-af46-45b8f1c3bc2a',
    'devil-square': '61c61a58-211e-4d6a-9ea1-d25e0c4a47c5',
    'chaos-castle': '3ad96a70-ed24-4979-80b8-169e461e548f',
}


@bp.route('/api/events/config', methods=['GET'])
def get_events_config():
    """Return the stored panel configuration (game masters only)."""
    if not is_admin_session():
        return jsonify({'success': False, 'message': 'Acesso restrito à equipe'}), 403

    try:
        return jsonify({'success': True, 'events': read_events_config() or []}), 200
    except Exception as error:
        logger.error(f"Error reading events configuration: {error}")
        return jsonify({'success': False, 'message': 'Erro ao carregar configuração'}), 500


@bp.route('/api/events/config', methods=['PUT', 'POST'])
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


@bp.route('/api/events/config', methods=['DELETE'])
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


def _stored_schedule_events(stored_events):
    """Convert the stored panel configuration into home-page event cards."""
    return [{
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


def _plugin_activity_by_event(cursor):
    """Map each mini-game to whether its automatic start plugin is active.

    Missing rows default to active, because these plugins are active by default.
    """
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
    return active_by_event


def _game_data_schedule_events():
    """Build the fallback event cards from the game's mini-game definitions."""
    conn = db.get_db_connection()
    try:
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
        active_by_event = _plugin_activity_by_event(cursor)
    finally:
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
    return events


@bp.route('/api/events/schedule', methods=['GET'])
def get_events_schedule():
    """Return the event cards shown in the "Eventos do Servidor" panel.

    When the staff panel (eventos.html) has a stored configuration, it is
    authoritative: the response carries the full list and "replace": true, so
    the home page renders exactly what the panel defines. Otherwise the
    mini-game definitions from the game database are returned as a partial
    overlay ("replace": false), which only adjusts the site's built-in cards.

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
        return jsonify({
            'success': True,
            'replace': True,
            'events': _stored_schedule_events(stored_events),
        }), 200

    try:
        return jsonify({
            'success': True,
            'replace': False,
            'events': _game_data_schedule_events(),
        }), 200
    except Exception as error:
        logger.error(f"Error getting event schedule: {error}")
        return jsonify({'success': False, 'events': []}), 500
