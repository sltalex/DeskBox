<?php
declare(strict_types=1);
define('DESKBOX_API_ENTRY', true);
require dirname(__DIR__, 2) . '/_app/bootstrap.php';

// Retired in favour of the optimistic-concurrency full-state endpoint. Keeping
// this route writable would allow old clients to bypass state_version checks.
api_json(['code' => 'endpoint_retired', 'message' => 'Use tree/state.php.'], 410);
