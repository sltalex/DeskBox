<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';

api_run('POST', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    auth_rate_limit($pdo, 'backup_create', hash('sha256', (string)$user['user_id']), 30, 3600, 'api_clock');
    $body = api_body(4 * 1024 * 1024, true, 16 * 1024 * 1024);
    $rawFiles = $body['files'] ?? null;
    if (!is_array($rawFiles)) { throw new ApiError('invalid_backup', 'Backup files are required.', 400); }
    $files = array_map(static fn(mixed $file): array => is_object($file) ? get_object_vars($file) : (is_array($file) ? $file : []), $rawFiles);
    return backup_create_snapshot(
        $pdo, (string)$user['user_id'], api_string($body, 'device_id'), api_string($body, 'app_version'), $files, 'api_clock'
    );
});
