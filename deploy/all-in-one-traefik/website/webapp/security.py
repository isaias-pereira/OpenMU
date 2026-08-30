"""Password hashing and input validation helpers."""
import re

import bcrypt

LOGIN_NAME_PATTERN = re.compile(r'^[a-zA-Z0-9]{3,10}$')
EMAIL_PATTERN = re.compile(r'^[^\s@]+@[^\s@]+\.[^\s@]+$')


def hash_password(password: str) -> str:
    """Hash password using BCrypt (compatible with OpenMU)."""
    salt = bcrypt.gensalt(rounds=12)
    hashed = bcrypt.hashpw(password.encode('utf-8'), salt)
    return hashed.decode('utf-8')


def check_password(password: str, password_hash: str) -> bool:
    """Verify a plaintext password against a BCrypt hash."""
    return bcrypt.checkpw(password.encode('utf-8'), password_hash.encode('utf-8'))


def validate_login_name(login_name: str) -> bool:
    """Validate login name: 3-10 alphanumeric characters."""
    return bool(LOGIN_NAME_PATTERN.match(login_name))


def validate_email(email: str) -> bool:
    """Basic email validation."""
    return bool(EMAIL_PATTERN.match(email))
