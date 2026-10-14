<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';

api_run('POST', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    $body = api_body();
    if (!array_key_exists('participating', $body) || !is_bool($body['participating'])) {
        throw new ApiError('invalid_profile', 'Participation setting is required.', 400);
    }
    return tree_update_profile($pdo, (string)$user['user_id'], $body['participating'], 'api_clock');
});
