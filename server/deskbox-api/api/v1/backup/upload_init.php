<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';
api_run('POST', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    auth_rate_limit($pdo, 'backup_upload_init', hash('sha256', (string)$user['user_id']), 10, 3600, 'api_clock');
    return backup_upload_init($pdo, (string)$user['user_id'], api_body(8192), 'api_clock');
});
