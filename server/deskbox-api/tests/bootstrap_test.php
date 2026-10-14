<?php
declare(strict_types=1);

define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__) . '/api/_app/bootstrap.php';

function bootstrap_expect(bool $condition, string $message): void
{
    if (!$condition) { throw new RuntimeException($message); }
}

function bootstrap_expect_error(string $code, int $status, callable $operation): void
{
    try {
        $operation();
    } catch (ApiError $error) {
        bootstrap_expect($error->apiCode === $code, "Expected {$code}, got {$error->apiCode}");
        bootstrap_expect($error->httpStatus === $status, "Expected HTTP {$status}, got {$error->httpStatus}");
        bootstrap_expect($error->getMessage() !== '', 'API errors include a message.');
        return;
    }
    throw new RuntimeException("Expected API error {$code}");
}

$body = api_decode_body('{"username":"Simon","password":"secret1"}');
bootstrap_expect($body['username'] === 'Simon', 'JSON objects are accepted.');
bootstrap_expect_error('invalid_json', 400, static fn() => api_decode_body('[]'));
bootstrap_expect_error('invalid_json', 400, static fn() => api_decode_body('[1]'));
bootstrap_expect_error('invalid_json', 400, static fn() => api_decode_body('{bad json'));
bootstrap_expect_error('request_too_large', 413, static fn() => api_decode_body(str_repeat('x', 8193)));

echo "bootstrap_test: ok\n";
