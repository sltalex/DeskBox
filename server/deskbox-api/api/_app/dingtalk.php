<?php
declare(strict_types=1);
if (!defined('DESKBOX_API_ENTRY')) { http_response_code(404); exit; }

function dingtalk_read(PDO $pdo, string $userId): array
{
    $s = $pdo->prepare("SELECT corp_id,dingtalk_user_id,display_name,organization_name,department_name,avatar_url,provider,created_at FROM account_identities WHERE user_id=? AND provider='dingtalk'");
    $s->execute([$userId]);
    $row = $s->fetch(PDO::FETCH_ASSOC);
    if (!$row) return ['identity' => null];
    $row['linked_at'] = auth_parse_sql_time($row['created_at'])->format(DateTimeInterface::ATOM);
    unset($row['created_at']);
    return ['identity' => $row];
}

function dingtalk_link(PDO $pdo, string $userId, array $body, callable $clock): array
{
    $values = [];
    foreach (['corp_id'=>128,'dingtalk_user_id'=>128,'display_name'=>255,'organization_name'=>255,'department_name'=>512,'avatar_url'=>2048] as $key=>$maximum) {
        $value = $body[$key] ?? '';
        if (!is_string($value) || strlen($value) > $maximum || preg_match('/[\x00-\x1f]/', $value))
            throw new ApiError('invalid_profile', 'Invalid profile field.', 400);
        $values[$key] = trim($value);
    }
    foreach (['corp_id','dingtalk_user_id'] as $key) {
        if (!preg_match('/\A[A-Za-z0-9_.:@-]{1,128}\z/', $values[$key]))
            throw new ApiError('invalid_profile', 'Invalid identity.', 400);
    }
    if ($values['avatar_url'] !== '' && (!filter_var($values['avatar_url'], FILTER_VALIDATE_URL) || parse_url($values['avatar_url'], PHP_URL_SCHEME) !== 'https'))
        throw new ApiError('invalid_profile', 'Invalid avatar URL.', 400);
    auth_rate_limit($pdo, 'dingtalk_link', hash('sha256', $userId), 30, 300, $clock);
    try {
        $pdo->beginTransaction();
        $lock = $pdo->getAttribute(PDO::ATTR_DRIVER_NAME) === 'mysql' ? ' FOR UPDATE' : '';
        $s = $pdo->prepare('SELECT id FROM auth_users WHERE id=?' . $lock);
        $s->execute([$userId]);
        $current = dingtalk_read($pdo, $userId)['identity'];
        if ($current && ($current['corp_id'] !== $values['corp_id'] || $current['dingtalk_user_id'] !== $values['dingtalk_user_id']))
            throw new ApiError('dingtalk_identity_changed', 'Unlink the previous identity before linking another.', 409);
        $now = auth_sql_time($clock());
        if ($current) {
            $s = $pdo->prepare("UPDATE account_identities SET display_name=?,organization_name=?,department_name=?,avatar_url=?,updated_at=? WHERE user_id=? AND provider='dingtalk'");
            $s->execute([$values['display_name'],$values['organization_name'],$values['department_name'],$values['avatar_url'],$now,$userId]);
        } else {
            $s = $pdo->prepare("INSERT INTO account_identities (id,user_id,provider,corp_id,dingtalk_user_id,display_name,organization_name,department_name,avatar_url,created_at,updated_at) VALUES (?,?,'dingtalk',?,?,?,?,?,?,?,?)");
            $s->execute([auth_random_id(),$userId,...array_values($values),$now,$now]);
        }
        $result = dingtalk_read($pdo, $userId);
        $pdo->commit();
        return $result;
    } catch (Throwable $e) {
        if ($pdo->inTransaction()) $pdo->rollBack();
        if ($e instanceof PDOException && auth_is_unique_violation($e))
            throw new ApiError('dingtalk_identity_taken', 'This identity is already linked.', 409);
        throw $e;
    }
}

function dingtalk_unlink(PDO $pdo, string $userId): array
{
    $s = $pdo->prepare("DELETE FROM account_identities WHERE user_id=? AND provider='dingtalk'");
    $s->execute([$userId]);
    return ['ok'=>true];
}
