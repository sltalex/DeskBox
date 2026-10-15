<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';
api_run('GET', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    $upload = backup_upload_read($pdo, (string)$user['user_id'], api_string($_GET, 'upload_id'));
    return ['upload_id' => (string)$upload['id'], 'chunk_size' => ACCOUNT_BACKUP_UPLOAD_CHUNK_BYTES, 'received' => backup_upload_received($pdo, (string)$upload['id'])];
});
