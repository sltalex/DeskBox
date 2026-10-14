CREATE TABLE auth_users (
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    username_normalized TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE auth_sessions (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    access_token_hash TEXT NOT NULL UNIQUE,
    refresh_token_hash TEXT NOT NULL UNIQUE,
    access_expires_at TEXT NOT NULL,
    refresh_expires_at TEXT NOT NULL,
    revoked_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE auth_rate_limits (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    bucket_key TEXT NOT NULL,
    action TEXT NOT NULL,
    window_started_at TEXT NOT NULL,
    attempts INTEGER NOT NULL,
    UNIQUE(bucket_key, action)
);
