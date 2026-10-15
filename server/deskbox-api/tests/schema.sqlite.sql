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

CREATE TABLE account_backup_snapshots (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    app_version TEXT NOT NULL,
    file_count INTEGER NOT NULL,
    total_bytes INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

CREATE TABLE account_backup_files (
    snapshot_id TEXT NOT NULL,
    relative_path TEXT NOT NULL,
    byte_count INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    json_content TEXT NOT NULL,
    PRIMARY KEY (snapshot_id, relative_path),
    FOREIGN KEY (snapshot_id) REFERENCES account_backup_snapshots(id) ON DELETE CASCADE
);

CREATE TABLE tree_profiles (
    user_id TEXT PRIMARY KEY,
    participation_enabled INTEGER NOT NULL DEFAULT 1,
    total_energy INTEGER NOT NULL DEFAULT 0,
    fruit_total INTEGER NOT NULL DEFAULT 0,
    harvested_total INTEGER NOT NULL DEFAULT 0,
    stage INTEGER NOT NULL DEFAULT 1,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

CREATE TABLE tree_daily_progress (
    user_id TEXT NOT NULL,
    progress_day TEXT NOT NULL,
    growth INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, progress_day),
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

CREATE TABLE tree_progress_operations (
    user_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    progress_day TEXT NOT NULL,
    energy_delta INTEGER NOT NULL,
    day_growth_delta INTEGER NOT NULL,
    fruit_delta INTEGER NOT NULL,
    harvested_delta INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    PRIMARY KEY (user_id, operation_id),
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

CREATE TABLE account_backup_uploads (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    app_version TEXT NOT NULL,
    total_bytes INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

CREATE TABLE account_backup_upload_parts (
    upload_id TEXT NOT NULL,
    part_start INTEGER NOT NULL,
    part_end INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    PRIMARY KEY (upload_id, part_start),
    FOREIGN KEY (upload_id) REFERENCES account_backup_uploads(id) ON DELETE CASCADE
);
