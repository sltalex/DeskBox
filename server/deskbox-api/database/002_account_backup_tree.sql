CREATE TABLE IF NOT EXISTS account_backup_snapshots (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    app_version VARCHAR(64) NOT NULL,
    file_count INT UNSIGNED NOT NULL,
    total_bytes BIGINT UNSIGNED NOT NULL,
    sha256 CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id), KEY ix_account_backup_user_created (user_id, created_at, id),
    CONSTRAINT fk_account_backup_user FOREIGN KEY (user_id) REFERENCES auth_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS account_backup_files (
    snapshot_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    relative_path VARCHAR(512) NOT NULL,
    byte_count INT UNSIGNED NOT NULL,
    sha256 CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    json_content LONGTEXT NOT NULL,
    PRIMARY KEY (snapshot_id, relative_path),
    CONSTRAINT fk_account_backup_file_snapshot FOREIGN KEY (snapshot_id) REFERENCES account_backup_snapshots (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS tree_profiles (
    user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    participation_enabled TINYINT(1) NOT NULL DEFAULT 1,
    total_energy BIGINT UNSIGNED NOT NULL DEFAULT 0,
    fruit_total BIGINT UNSIGNED NOT NULL DEFAULT 0,
    harvested_total BIGINT UNSIGNED NOT NULL DEFAULT 0,
    stage TINYINT UNSIGNED NOT NULL DEFAULT 1,
    updated_at DATETIME NOT NULL,
    PRIMARY KEY (user_id), KEY ix_tree_rank (participation_enabled, total_energy, updated_at),
    CONSTRAINT fk_tree_profile_user FOREIGN KEY (user_id) REFERENCES auth_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS tree_daily_progress (
    user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    progress_day DATE NOT NULL,
    growth BIGINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, progress_day), KEY ix_tree_daily_day (progress_day, growth),
    CONSTRAINT fk_tree_daily_user FOREIGN KEY (user_id) REFERENCES auth_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS tree_progress_operations (
    user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    operation_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    progress_day DATE NOT NULL,
    energy_delta BIGINT UNSIGNED NOT NULL,
    day_growth_delta BIGINT UNSIGNED NOT NULL,
    fruit_delta BIGINT UNSIGNED NOT NULL,
    harvested_delta BIGINT UNSIGNED NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (user_id, operation_id), KEY ix_tree_operations_created (created_at),
    CONSTRAINT fk_tree_operation_user FOREIGN KEY (user_id) REFERENCES auth_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
