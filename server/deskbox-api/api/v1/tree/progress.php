<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';

api_run('POST', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    auth_rate_limit($pdo, 'tree_progress', hash('sha256', (string)$user['user_id']), 120, 3600, 'api_clock');
    return tree_apply_progress($pdo, (string)$user['user_id'], api_body(), 'api_clock');
});
