CREATE TABLE IF NOT EXISTS auth_users (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    username VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    username_normalized VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    password_hash VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NOT NULL,
    PRIMARY KEY (id), UNIQUE KEY ux_auth_users_username_normalized (username_normalized)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS auth_sessions (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    access_token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    refresh_token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    access_expires_at DATETIME NOT NULL, refresh_expires_at DATETIME NOT NULL,
    revoked_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL,
    PRIMARY KEY (id), UNIQUE KEY ux_auth_sessions_access_hash (access_token_hash),
    UNIQUE KEY ux_auth_sessions_refresh_hash (refresh_token_hash), KEY ix_auth_sessions_user (user_id),
    KEY ix_auth_sessions_refresh_expiry (refresh_expires_at),
    CONSTRAINT fk_auth_sessions_user FOREIGN KEY (user_id) REFERENCES auth_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS auth_rate_limits (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    bucket_key CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    action VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    window_started_at DATETIME NOT NULL, attempts INT UNSIGNED NOT NULL,
    PRIMARY KEY (id), UNIQUE KEY ux_auth_rate_limits_bucket_action (bucket_key, action),
    KEY ix_auth_rate_limits_window (window_started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
