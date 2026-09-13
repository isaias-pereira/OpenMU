"""Route blueprints for the website service."""
from . import accounts, events, misc, pages, rankings, vip

ALL_BLUEPRINTS = (
    pages.bp,
    accounts.bp,
    events.bp,
    rankings.bp,
    misc.bp,
    vip.bp,
)
