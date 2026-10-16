CREATE TABLE IF NOT EXISTS account_identities (
 id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 provider VARCHAR(16) NOT NULL,
 corp_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 dingtalk_user_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 display_name VARCHAR(255) NOT NULL,
 organization_name VARCHAR(255) NOT NULL,
 department_name VARCHAR(512) NOT NULL,
 avatar_url VARCHAR(2048) NOT NULL DEFAULT '',
 raw_scope_summary VARCHAR(255) NOT NULL DEFAULT 'client_reported_basic_profile',
 created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL,
 UNIQUE KEY ux_identity_owner (user_id, provider),
 UNIQUE KEY ux_identity_provider (provider, corp_id, dingtalk_user_id),
 FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
