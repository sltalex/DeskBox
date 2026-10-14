<?php
declare(strict_types=1);

define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__) . '/api/_app/auth.php';

function expect_true(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function expect_api_error(string $code, callable $operation): void
{
    try {
        $operation();
    } catch (ApiError $error) {
        expect_true($error->apiCode === $code, "Expected {$code}, got {$error->apiCode}");
        return;
    }
    throw new RuntimeException("Expected API error {$code}");
}

$pdo = new PDO('sqlite::memory:');
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
$pdo->exec(file_get_contents(__DIR__ . '/schema.sqlite.sql'));
$clock = static fn(): DateTimeImmutable => new DateTimeImmutable('2026-10-14T00:00:00Z');

$registered = auth_register($pdo, 'Simon_01', 'secret1', 'device-a', '127.0.0.1', $clock);
expect_true($registered['user']['username'] === 'Simon_01', 'Registration preserves display casing.');
expect_true(isset($registered['session']['access_token'], $registered['session']['refresh_token']), 'Registration issues a session.');

expect_api_error('username_taken', static fn() => auth_register($pdo, 'simon_01', 'secret2', 'device-b', '127.0.0.2', $clock));
expect_api_error('invalid_credentials', static fn() => auth_login($pdo, 'Simon_01', 'wrong!', 'device-a', '127.0.0.1', $clock));

$loggedIn = auth_login($pdo, 'SIMON_01', 'secret1', 'device-b', '127.0.0.1', $clock);
$oldRefresh = $loggedIn['session']['refresh_token'];
$refreshed = auth_refresh($pdo, $oldRefresh, $clock);
expect_true($refreshed['session']['refresh_token'] !== $oldRefresh, 'Refresh token rotates.');
expect_api_error('invalid_session', static fn() => auth_refresh($pdo, $oldRefresh, $clock));

$me = auth_me($pdo, $refreshed['session']['access_token'], $clock);
expect_true($me['user']['username'] === 'Simon_01', 'Access token resolves the user.');
auth_logout($pdo, $refreshed['session']['access_token'], $clock);
expect_api_error('invalid_session', static fn() => auth_me($pdo, $refreshed['session']['access_token'], $clock));

for ($attempt = 0; $attempt < 5; $attempt++) {
    expect_api_error('invalid_credentials', static fn() => auth_login($pdo, 'nobody', 'secret1', 'device-x', '10.0.0.1', $clock));
}
expect_api_error('rate_limited', static fn() => auth_login($pdo, 'nobody', 'secret1', 'device-x', '10.0.0.1', $clock));

expect_api_error('invalid_username', static fn() => auth_register($pdo, 'ab', 'secret1', 'device-a', '127.0.0.1', $clock));
expect_api_error('invalid_password', static fn() => auth_register($pdo, 'valid_name', '12345', 'device-a', '127.0.0.1', $clock));
expect_api_error('invalid_username', static fn() => auth_register($pdo, str_repeat('a', 33), 'secret1', 'device-a', '127.0.0.3', $clock));
expect_api_error('invalid_password', static fn() => auth_register($pdo, 'long_password', str_repeat('x', 129), 'device-a', '127.0.0.3', $clock));

$minimum = auth_register($pdo, 'abc', '123456', 'device-min', '127.0.0.4', $clock);
expect_true($minimum['user']['username'] === 'abc', 'Minimum username and password lengths are accepted.');
$maximum = auth_register($pdo, str_repeat('z', 32), str_repeat('p', 128), 'device-max', '127.0.0.5', $clock);
expect_true(strlen($maximum['user']['username']) === 32, 'Maximum username and password lengths are accepted.');

$expiredAccessClock = static fn(): DateTimeImmutable => new DateTimeImmutable('2026-10-14T00:16:00Z');
expect_api_error(
    'invalid_session',
    static fn() => auth_me($pdo, $maximum['session']['access_token'], $expiredAccessClock)
);
$expiredRefreshClock = static fn(): DateTimeImmutable => new DateTimeImmutable('2026-11-14T00:00:01Z');
expect_api_error(
    'invalid_session',
    static fn() => auth_refresh($pdo, $maximum['session']['refresh_token'], $expiredRefreshClock)
);

$pdo->exec("CREATE TRIGGER reject_auth_session BEFORE INSERT ON auth_sessions BEGIN SELECT RAISE(ABORT, 'session rejected'); END");
try {
    auth_register($pdo, 'rollback_user', 'secret1', 'device-r', '127.0.0.6', $clock);
    throw new RuntimeException('Expected session creation failure.');
} catch (Throwable $error) {
    expect_true(!($error instanceof RuntimeException) || $error->getMessage() !== 'Expected session creation failure.', 'Session creation fails.');
}
$rollbackStatement = $pdo->prepare('SELECT COUNT(*) FROM auth_users WHERE username_normalized = :username');
$rollbackStatement->execute([':username' => 'rollback_user']);
expect_true((int)$rollbackStatement->fetchColumn() === 0, 'Registration rolls back the user when session creation fails.');

echo "auth_service_test: ok\n";
