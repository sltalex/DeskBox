<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';
api_run('PUT', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    $uploadId = api_string($_GET, 'upload_id');
    return backup_upload_chunk($pdo, (string)$user['user_id'], $uploadId, 'api_clock');
});
