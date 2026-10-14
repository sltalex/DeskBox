<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
api_run('POST', static function (PDO $pdo): array {
    $body = api_body();
    return auth_refresh($pdo, api_string($body, 'refresh_token'), 'api_clock');
});
