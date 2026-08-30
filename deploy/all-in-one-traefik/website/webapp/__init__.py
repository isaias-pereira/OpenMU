"""Website service package: builds the Flask application."""
import logging

from flask import Flask
from flask_cors import CORS

from .config import resolve_secret_key
from .routes import ALL_BLUEPRINTS

logging.basicConfig(level=logging.INFO)


def create_app() -> Flask:
    """Create and configure the Flask application.

    static_folder is disabled on purpose: Flask's built-in static rule is also
    "/<path:filename>", and it would shadow the static_files() catch-all in the
    pages blueprint — which is what gates /eventos.html and falls back to
    public/. All file serving goes through static_files() so those rules cannot
    be bypassed.
    """
    app = Flask(__name__, static_folder=None)
    CORS(app)
    app.secret_key = resolve_secret_key()

    for blueprint in ALL_BLUEPRINTS:
        app.register_blueprint(blueprint)

    return app


app = create_app()
