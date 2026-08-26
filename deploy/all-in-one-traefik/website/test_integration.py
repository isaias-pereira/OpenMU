"""End-to-end test of the events panel integration, with an in-memory fake database.

Run:  ./.venv-test/Scripts/python.exe test_integration.py
"""
import json
import sys

# ---------------------------------------------------------------------------
# Fake database that understands only the statements app.py issues.
# ---------------------------------------------------------------------------
STORE = {}          # SiteConfig rows: key -> value(json string)
ACCOUNT = ['gmuser', 2]   # LoginName, State (2 = GameMaster)
MINIGAME_ROWS = [
    ('blood-castle', 'Blood Castle 1', 120, 900),
    ('devil-square', 'Devil Square 1', 180, 1200),
    ('chaos-castle', 'Chaos Castle 1', 60, None),
]
PLUGIN_ROWS = [
    ('95e68c14-ad87-4b3c-af46-45b8f1c3bc2a', True),
    ('61c61a58-211e-4d6a-9ea1-d25e0c4a47c5', True),
    ('3ad96a70-ed24-4979-80b8-169e461e548f', True),
]


class FakeCursor:
    def __init__(self):
        self._result = []

    def execute(self, query, params=None):
        q = ' '.join(query.split())
        if 'FROM "data"."Account"' in q and '"State"' in q:
            self._result = [tuple(ACCOUNT)] if ACCOUNT else []
        elif 'CREATE SCHEMA' in q or 'CREATE TABLE' in q:
            self._result = []
        elif 'SELECT "Value" FROM "website"."SiteConfig"' in q:
            value = STORE.get(params[0])
            self._result = [(value,)] if value is not None else []
        elif 'INSERT INTO "website"."SiteConfig"' in q:
            STORE[params[0]] = params[1]
            self._result = []
        elif 'DELETE FROM "website"."SiteConfig"' in q:
            STORE.pop(params[0], None)
            self._result = []
        elif 'FROM "config"."MiniGameDefinition"' in q:
            self._result = list(MINIGAME_ROWS)
        elif 'FROM "config"."PlugInConfiguration"' in q:
            self._result = list(PLUGIN_ROWS)
        else:
            raise AssertionError('Unexpected query: ' + q[:160])

    def fetchall(self):
        return self._result

    def fetchone(self):
        return self._result[0] if self._result else None


class FakeConn:
    def cursor(self):
        return FakeCursor()

    def commit(self):
        pass

    def rollback(self):
        pass

    def close(self):
        pass


import app as site
site.get_db_connection = lambda: FakeConn()
site.app.config.update(TESTING=True)
client = site.app.test_client()

failures = []


def check(label, condition, detail=''):
    status = 'PASS' if condition else 'FAIL'
    print(f'[{status}] {label}' + (f' -> {detail}' if detail and not condition else ''))
    if not condition:
        failures.append(label)


def as_admin():
    with client.session_transaction() as sess:
        sess['account_id'] = '11111111-1111-1111-1111-111111111111'


def as_anonymous():
    with client.session_transaction() as sess:
        sess.clear()


print('\n=== 1. Anonymous access is blocked ===')
as_anonymous()
r = client.get('/eventos.html')
check('/eventos.html rejects anonymous with 401', r.status_code == 401, r.status_code)
check('/eventos.html returns the login gate', b'Acesso restrito' in r.data)
check('gate does not leak the panel', b'eventsCardsGrid' not in r.data)

r = client.get('/public/eventos.html')
check('/public/eventos.html is gated too', r.status_code == 401, r.status_code)

r = client.get('/api/events/config')
check('GET /api/events/config forbidden (403)', r.status_code == 403, r.status_code)
r = client.put('/api/events/config', json={'events': [{'name': 'x'}]})
check('PUT /api/events/config forbidden (403)', r.status_code == 403, r.status_code)
r = client.delete('/api/events/config')
check('DELETE /api/events/config forbidden (403)', r.status_code == 403, r.status_code)

r = client.get('/api/session')
check('/api/session reports anonymous', r.get_json()['loggedIn'] is False)

print('\n=== 2. Non-admin (normal player) is blocked ===')
ACCOUNT[:] = ['player1', 0]
as_admin()   # a valid session, but the account is a normal player
r = client.get('/eventos.html')
check('normal player cannot open the panel', r.status_code == 401, r.status_code)
r = client.get('/api/session')
body = r.get_json()
check('/api/session says logged in but not admin', body['loggedIn'] and not body['isAdmin'], body)

print('\n=== 3. Static assets fall back to public/ ===')
for path in ('css/eventos.css', 'js/eventos.js'):
    r = client.get('/' + path)
    check(f'/{path} served from public/', r.status_code == 200, r.status_code)
r = client.get('/favicon.ico')
check('/favicon.ico still served from root', r.status_code == 200, r.status_code)
r = client.get('/..%2fapp.py')
check('path traversal refused', r.status_code in (400, 404), r.status_code)

print('\n=== 4. Game master can open the panel ===')
ACCOUNT[:] = ['gmuser', 2]
as_admin()
r = client.get('/eventos.html')
check('GM gets the panel (200)', r.status_code == 200, r.status_code)
check('panel HTML is the real one', b'eventsCardsGrid' in r.data)

r = client.get('/api/events/config')
check('GET config OK and empty initially', r.status_code == 200 and r.get_json()['events'] == [])

print('\n=== 5. Home page falls back to game data while nothing is published ===')
r = client.get('/api/events/schedule')
body = r.get_json()
check('schedule replace=False before publishing', body['replace'] is False, body)
check('schedule returns the 3 mini-games', len(body['events']) == 3, body)
check('mini-games are fixed (countdown, not "Aguardando plugin")',
      all(e['scheduleMode'] == 'fixed' for e in body['events']), body)

print('\n=== 6. Publishing from the panel drives the home page ===')
payload = {'events': [
    {'id': 'evt-bc', 'name': 'Blood Castle', 'category': 'events', 'icon': '🩸',
     'colorTheme': '#f43f5e', 'location': 'Devias', 'frequency': 'A cada 2 horas',
     'startTimeStr': '00:00', 'startIntervalMin': 120, 'startOffsetMin': 0,
     'durationMin': 15, 'rewardTag': 'Arma do Arcanjo', 'enabled': True},
    {'id': 'evt-dragon', 'name': 'Invasão de Dragões', 'category': 'invasions', 'icon': '🐲',
     'colorTheme': '#f97316', 'location': 'Lorencia', 'frequency': 'A cada 4 horas',
     'startIntervalMin': 240, 'startOffsetMin': 0, 'durationMin': 10,
     'rewardTag': 'Box of Kundun', 'enabled': True},
    {'id': 'evt-hidden', 'name': 'Evento Oculto', 'category': 'events',
     'startIntervalMin': 60, 'startOffsetMin': 5, 'durationMin': 5, 'enabled': False},
]}
r = client.put('/api/events/config', json=payload)
check('PUT config accepted', r.status_code == 200, r.get_json())

r = client.get('/api/events/schedule')
body = r.get_json()
check('schedule now replace=True', body['replace'] is True, body)
ids = [e['id'] for e in body['events']]
check('disabled event hidden from home page', ids == ['evt-bc', 'evt-dragon'], ids)
check('invasion category preserved',
      body['events'][1]['category'] == 'invasions', body['events'][1])
check('custom event reaches the home page with its schedule',
      body['events'][0]['startIntervalMin'] == 120 and body['events'][0]['durationMin'] == 15,
      body['events'][0])
check('every published card is fixed mode',
      all(e['scheduleMode'] == 'fixed' for e in body['events']))

print('\n=== 7. Anonymous visitors see the published schedule ===')
as_anonymous()
r = client.get('/api/events/schedule')
body = r.get_json()
check('public schedule is the published one', body['replace'] is True and len(body['events']) == 2, body)

print('\n=== 8. Validation and clamping ===')
ACCOUNT[:] = ['gmuser', 3]   # GameMasterInvisible also allowed
as_admin()
r = client.put('/api/events/config', json={'events': []})
check('empty list refused (400)', r.status_code == 400, r.status_code)
r = client.put('/api/events/config', json={'events': [{'name': '  '}]})
check('nameless event refused (400)', r.status_code == 400, r.status_code)
r = client.put('/api/events/config', json={'events': [
    {'id': 'dup', 'name': 'A'}, {'id': 'dup', 'name': 'B'}]})
check('duplicate ids refused (400)', r.status_code == 400, r.status_code)
r = client.put('/api/events/config', json={'events': [
    {'name': 'Bad', 'startIntervalMin': 'abc'}]})
check('non numeric schedule refused (400)', r.status_code == 400, r.status_code)

r = client.put('/api/events/config', json={'events': [
    {'name': 'Clamp', 'startIntervalMin': 0, 'startOffsetMin': -5, 'durationMin': 99999,
     'category': 'weird', 'evil': '<script>'}]})
saved = r.get_json()['events'][0]
check('interval clamped to a sane value', saved['startIntervalMin'] == 120, saved)
check('negative offset clamped to 0', saved['startOffsetMin'] == 0, saved)
check('duration cannot exceed the interval', saved['durationMin'] == 120, saved)
check('unknown category normalized to events', saved['category'] == 'events', saved)
check('unknown fields dropped', 'evil' not in saved, saved)
check('generated id when missing', saved['id'].startswith('evt-'), saved)

print('\n=== 9. Reset restores the game-data fallback ===')
r = client.delete('/api/events/config')
check('DELETE config OK', r.status_code == 200, r.status_code)
as_anonymous()
body = client.get('/api/events/schedule').get_json()
check('schedule back to replace=False', body['replace'] is False, body)
check('mini-games returned again', len(body['events']) == 3, body)

print('\n=== 10. Secret key is not the old hardcoded value ===')
check('secret key is not "change-this-secret-key"',
      site.app.secret_key != 'change-this-secret-key', site.app.secret_key)

print('\n' + '=' * 60)
if failures:
    print(f'{len(failures)} FAILURE(S): ' + ', '.join(failures))
    sys.exit(1)
print('ALL CHECKS PASSED')
