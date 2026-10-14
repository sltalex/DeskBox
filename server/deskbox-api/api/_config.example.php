<?php
declare(strict_types=1);

if (!defined('DESKBOX_API_ENTRY')) { http_response_code(404); exit; }

// Copy this file to _config.local.php in the same directory on the server,
// fill in the private values there, and keep that local file outside source control.
return [
    'dsn' => 'mysql:host=127.0.0.1;port=3306;dbname=deskbox;charset=utf8mb4',
    'username' => 'deskbox',
    'password' => 'replace-on-server',
];
