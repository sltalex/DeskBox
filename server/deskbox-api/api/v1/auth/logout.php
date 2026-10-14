<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
api_run('POST', static function (PDO $pdo): array {
    auth_logout($pdo, api_bearer_token(), 'api_clock');
    return ['ok' => true];
});
