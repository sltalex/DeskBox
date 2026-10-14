<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';
api_run('GET', static fn(PDO $pdo): array => auth_me($pdo, api_bearer_token(), 'api_clock'));
