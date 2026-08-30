"""Persistence and validation of the staff events panel configuration.

The configuration lives in the database (schema "website") instead of a file
because the website container has no persistent volume.
"""
import json
import logging
import uuid
from datetime import datetime, timezone

from psycopg2 import Error as PGError

from . import db

logger = logging.getLogger(__name__)

# Key under which the events panel configuration is stored.
EVENTS_CONFIG_KEY = 'server_events'

# Fields of a single event, kept in sync with public/js/eventos.js and with the
# event cards of the home page.
EVENT_TEXT_FIELDS = ('id', 'name', 'category', 'icon', 'colorTheme', 'location',
                     'frequency', 'startTimeStr', 'rewardTag', 'description')
EVENT_INT_FIELDS = ('startIntervalMin', 'startOffsetMin', 'durationMin')

MAX_EVENTS = 60
MAX_TEXT_LENGTH = 300
MAX_NAME_LENGTH = 120
MAX_ID_LENGTH = 60
MAX_INTERVAL_MIN = 10080  # one week


def ensure_site_config_table(cursor):
    """Create the website's own configuration table if it does not exist yet.

    It lives in a separate "website" schema so it never collides with the
    schemas that OpenMU manages through its own migrations.
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
    conn = db.get_db_connection()
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
    conn = db.get_db_connection()
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
    conn = db.get_db_connection()
    try:
        cursor = conn.cursor()
        ensure_site_config_table(cursor)
        cursor.execute('DELETE FROM "website"."SiteConfig" WHERE "Key" = %s',
                       (EVENTS_CONFIG_KEY,))
        conn.commit()
    except PGError:
        conn.rollback()
        raise
    finally:
        conn.close()


def sanitize_events(raw_events):
    """Validate and normalize the event list coming from the panel.

    Only known fields are kept, so the panel can never store arbitrary data
    that later gets rendered on the public home page.
    """
    if not isinstance(raw_events, list) or not raw_events:
        raise ValueError('Envie ao menos um evento')

    if len(raw_events) > MAX_EVENTS:
        raise ValueError(f'Limite de {MAX_EVENTS} eventos excedido')

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
                event[field] = str(value)[:MAX_TEXT_LENGTH]

        event['name'] = name[:MAX_NAME_LENGTH]
        event['category'] = 'invasions' if event.get('category') == 'invasions' else 'events'

        event_id = (event.get('id') or '').strip() or f'evt-{uuid.uuid4().hex[:8]}'
        if event_id in seen_ids:
            raise ValueError(f'Evento duplicado: {event_id}')
        seen_ids.add(event_id)
        event['id'] = event_id[:MAX_ID_LENGTH]

        for field in EVENT_INT_FIELDS:
            try:
                event[field] = int(float(raw.get(field, 0) or 0))
            except (TypeError, ValueError):
                raise ValueError(f'Valor numérico inválido em "{field}"')

        # Keep the schedule maths safe for the countdown on the home page.
        event['startIntervalMin'] = min(max(event['startIntervalMin'] or 120, 1), MAX_INTERVAL_MIN)
        event['startOffsetMin'] = min(max(event['startOffsetMin'], 0), MAX_INTERVAL_MIN - 1)
        event['durationMin'] = min(max(event['durationMin'] or 1, 1), event['startIntervalMin'])
        event['enabled'] = raw.get('enabled') is not False
        events.append(event)

    return events
