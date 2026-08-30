"""HTML pages and static file serving."""
import os

from flask import Blueprint, jsonify, send_from_directory

from ..auth import is_admin_session
from ..config import PUBLIC_ROOT, SITE_ROOT

bp = Blueprint('pages', __name__)


@bp.route('/')
def index():
    """Serve the main website."""
    return send_from_directory(SITE_ROOT, 'index.html')


@bp.route('/eventos.html')
def events_panel():
    """Serve the staff events panel, but only to signed-in game masters."""
    if not is_admin_session():
        return jsonify({'success': False, 'message': 'Acesso restrito à equipe'}), 401
    return send_from_directory(PUBLIC_ROOT, 'eventos.html')


@bp.route('/<path:path>')
def static_files(path):
    """Serve static files.

    Files placed in "public" are also served from the root, mirroring what the
    Angular build does with that folder, so the panel's assets keep working
    without having to duplicate them.
    """
    if '..' in path:
        return jsonify({'success': False, 'message': 'Caminho inválido'}), 400

    # The panel itself must always go through the authenticated route above.
    if path.lower() in ('eventos.html', 'public/eventos.html'):
        return events_panel()

    if os.path.isfile(os.path.join(SITE_ROOT, path)):
        return send_from_directory(SITE_ROOT, path)

    if os.path.isfile(os.path.join(PUBLIC_ROOT, path)):
        return send_from_directory(PUBLIC_ROOT, path)

    return send_from_directory(SITE_ROOT, path)
