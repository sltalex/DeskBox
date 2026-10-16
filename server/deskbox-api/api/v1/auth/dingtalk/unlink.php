<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 3) . '/_app/bootstrap.php';
require dirname(__DIR__, 3) . '/_app/dingtalk.php';
api_run('POST', static function(PDO $pdo): array {
    $session = auth_find_access_session($pdo, api_bearer_token(), api_clock());
    return dingtalk_unlink($pdo, $session['user_id']);
});
