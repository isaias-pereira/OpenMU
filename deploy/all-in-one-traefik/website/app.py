"""Entrypoint for the website service.

The application itself lives in the ``webapp`` package; this module only
exposes the Flask ``app`` object for gunicorn (``gunicorn app:app``) and the
``__main__`` runner for local development.
"""
import os

from webapp import app

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 5000))
    debug = os.environ.get('FLASK_DEBUG', 'false').lower() == 'true'
    app.run(host='0.0.0.0', port=port, debug=debug)
