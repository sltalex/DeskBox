<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
api_run('POST', static function (PDO $pdo): array {
    $body = api_body();
    return auth_register($pdo, api_string($body, 'username'), api_string($body, 'password'), api_string($body, 'device_id'), api_remote_address(), 'api_clock');
});
