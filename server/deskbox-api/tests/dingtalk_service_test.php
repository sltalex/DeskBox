<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__) . '/api/_app/auth.php';
require dirname(__DIR__) . '/api/_app/dingtalk.php';
$pdo = new PDO('sqlite::memory:');
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
$pdo->exec(file_get_contents(__DIR__ . '/schema.sqlite.sql'));
$clock = static fn(): DateTimeImmutable => new DateTimeImmutable('2026-10-15T00:00:00Z');
$account = auth_register($pdo, 'dws_test', 'secret1', 'device', '127.0.0.1', $clock);
$userId = $account['user']['id'];
$linked = dingtalk_link($pdo, $userId, [
    'corp_id' => 'corp-1', 'dingtalk_user_id' => 'user-1', 'display_name' => '张三',
    'organization_name' => 'SltBox', 'department_name' => '研发', 'avatar_url' => ''
], $clock);
if ($linked['identity']['dingtalk_user_id'] !== 'user-1') throw new RuntimeException('link failed');
$refreshed = dingtalk_link($pdo, $userId, [
    'corp_id' => 'corp-1', 'dingtalk_user_id' => 'user-1', 'display_name' => '李四',
    'organization_name' => 'SltBox', 'department_name' => '产品', 'avatar_url' => ''
], $clock);
if ($refreshed['identity']['display_name'] !== '李四') throw new RuntimeException('refresh failed');
dingtalk_unlink($pdo, $userId);
if (dingtalk_read($pdo, $userId)['identity'] !== null) throw new RuntimeException('unlink failed');
echo "dingtalk_service_test: ok\n";
