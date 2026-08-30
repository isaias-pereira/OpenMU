"""Ranking endpoints (level, deaths, resets and mini-game scores)."""
import logging

from flask import Blueprint, jsonify

from .. import db
from ..leveling import calculate_level_from_experience

logger = logging.getLogger(__name__)

bp = Blueprint('rankings', __name__)

RANKING_LIMIT = 5

# Shared FROM/JOIN skeleton for character rankings.
_CHARACTER_JOINS = '''
    FROM "data"."Character" c
    LEFT JOIN "config"."CharacterClass" cc ON cc."Id" = c."CharacterClassId"
    LEFT JOIN "guild"."GuildMember" gm ON c."Id" = gm."Id"
    LEFT JOIN "guild"."Guild" g ON gm."GuildId" = g."Id"
'''

_RESETS_SUBQUERY = '''
    COALESCE((SELECT sa."Value" FROM "data"."StatAttribute" sa
        JOIN "config"."AttributeDefinition" ad ON ad."Id" = sa."DefinitionId"
        WHERE sa."CharacterId" = c."Id" AND ad."Designation" = 'Resets' LIMIT 1), 0)
'''

EVENT_RANKING_PATTERNS = {
    'blood-castle': '%Blood Castle%',
    'devil-square': '%Devil Square%',
}


def _fetch_all(query, params=None):
    """Run a read-only query and return all rows, always closing the connection."""
    conn = db.get_db_connection()
    try:
        cursor = conn.cursor()
        cursor.execute(query, params)
        return cursor.fetchall()
    finally:
        conn.close()


@bp.route('/api/ranking', methods=['GET'])
def get_ranking():
    """Get top characters by level (experience)."""
    try:
        rows = _fetch_all(
            '''
            SELECT
                c."Name" as character_name,
                COALESCE(g."Name", '-') as guild_name,
                c."Experience" as experience,
                COALESCE(cc."Name", '-') as class_name,
                ''' + _RESETS_SUBQUERY + ''' as resets
            ''' + _CHARACTER_JOINS + '''
            WHERE c."CharacterStatus" = 0
            ORDER BY c."Experience" DESC
            LIMIT %s
            ''', (RANKING_LIMIT,))

        ranking = [{
            'position': i,
            'characterName': row[0],
            'guildName': row[1] if row[1] else '-',
            'level': calculate_level_from_experience(row[2] or 0),
            'className': row[3],
            'resets': int(row[4] or 0)
        } for i, row in enumerate(rows, 1)]

        return jsonify({'success': True, 'ranking': ranking}), 200

    except Exception as e:
        logger.error(f"Error getting ranking: {e}")
        return jsonify({
            'success': False,
            'message': 'Erro ao carregar ranking',
            'ranking': []
        }), 500


@bp.route('/api/ranking/deaths', methods=['GET'])
def get_deaths_ranking():
    """Get top characters by death count (PlayerKillCount)."""
    try:
        rows = _fetch_all(
            '''
            SELECT
                c."Name" as character_name,
                COALESCE(g."Name", '-') as guild_name,
                c."PlayerKillCount" as kill_count,
                COALESCE(cc."Name", '-') as class_name
            ''' + _CHARACTER_JOINS + '''
            WHERE c."CharacterStatus" = 0
            ORDER BY c."PlayerKillCount" DESC
            LIMIT %s
            ''', (RANKING_LIMIT,))

        ranking = [{
            'position': i,
            'characterName': row[0],
            'guildName': row[1] if row[1] else '-',
            'kills': int(row[2]) if row[2] else 0,
            'className': row[3]
        } for i, row in enumerate(rows, 1)]

        return jsonify({'success': True, 'ranking': ranking}), 200

    except Exception as e:
        logger.error(f"Error getting deaths ranking: {e}")
        return jsonify({
            'success': False,
            'message': 'Erro ao carregar ranking de mortes',
            'ranking': []
        }), 500


@bp.route('/api/ranking/resets', methods=['GET'])
def get_resets_ranking():
    """Get top characters by the persisted Resets attribute."""
    try:
        rows = _fetch_all(
            '''
            SELECT c."Name", COALESCE(g."Name", '-'), COALESCE(cc."Name", '-'),
            ''' + _RESETS_SUBQUERY + _CHARACTER_JOINS + '''
            WHERE c."CharacterStatus" = 0
            ORDER BY 4 DESC, c."Name"
            LIMIT %s
            ''', (RANKING_LIMIT,))

        return jsonify({'success': True, 'ranking': [{
            'position': index, 'characterName': row[0], 'guildName': row[1],
            'className': row[2], 'resets': int(row[3] or 0)
        } for index, row in enumerate(rows, 1)]}), 200
    except Exception as error:
        logger.error(f"Error getting reset ranking: {error}")
        return jsonify({'success': False, 'ranking': []}), 500


def get_minigame_ranking(name_pattern):
    """Return persisted ranking entries for configured mini-games."""
    rows = _fetch_all(
        '''
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
        LIMIT %s
        ''', (name_pattern, RANKING_LIMIT))
    return [{
        'position': index,
        'characterName': row[0],
        'guildName': row[1],
        'className': row[2],
        'score': int(row[3] or 0),
        'extraStat': f'{int(row[3] or 0)} pontos',
        'subStat': 'Ranking persistido do servidor'
    } for index, row in enumerate(rows, 1)]


@bp.route('/api/ranking/events/<event_name>', methods=['GET'])
def get_event_ranking(event_name):
    if event_name not in EVENT_RANKING_PATTERNS:
        return jsonify({'success': False, 'message': 'Evento inválido', 'ranking': []}), 404
    try:
        return jsonify({
            'success': True,
            'ranking': get_minigame_ranking(EVENT_RANKING_PATTERNS[event_name])
        }), 200
    except Exception as error:
        logger.error(f"Error getting {event_name} ranking: {error}")
        return jsonify({'success': False, 'ranking': []}), 500
