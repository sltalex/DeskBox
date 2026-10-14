<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';

api_run('GET', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    return backup_list_snapshots($pdo, (string)$user['user_id']);
});
