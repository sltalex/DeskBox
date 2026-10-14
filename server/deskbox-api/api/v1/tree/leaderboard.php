<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
require dirname(__DIR__, 2) . '/_app/account_data.php';

api_run('GET', static function (PDO $pdo): array {
    $user = account_authenticated_user($pdo, 'api_clock');
    $day = isset($_GET['day']) && is_string($_GET['day']) ? $_GET['day'] : api_clock()->format('Y-m-d');
    return tree_leaderboard($pdo, (string)$user['user_id'], $day);
});
