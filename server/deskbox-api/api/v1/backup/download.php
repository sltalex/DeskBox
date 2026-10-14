<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';

api_run('GET', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    $snapshotId = isset($_GET['snapshot_id']) && is_string($_GET['snapshot_id']) ? $_GET['snapshot_id'] : '';
    return backup_download_snapshot($pdo, (string)$user['user_id'], $snapshotId);
});
