# Tests for wayback-fetch.ps1. No Pester needed; runs on Windows PowerShell 5.1 and PowerShell 7.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\wayback-fetch\tests\run-tests.ps1
#   pwsh -NoProfile -File tools/wayback-fetch/tests/run-tests.ps1
#
# Unit tests use canned responses (no network). The end-to-end test starts tests\mock_archive.py (needs Python 3)
# on 127.0.0.1 and runs the real script against it twice; it is skipped when Python is missing (-RequireE2E fails it).
param(
    [switch]$RequireE2E,
    [string]$Python = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$script = Join-Path (Split-Path -Parent $here) 'wayback-fetch.ps1'
. $script

$script:Failures = 0
$script:Passes = 0
function Assert-Equal($expected, $actual, [string]$what) {
    if ("$expected" -ceq "$actual") { $script:Passes++ }
    else { $script:Failures++; Write-Host ("FAIL {0}: expected <{1}> got <{2}>" -f $what, $expected, $actual) -ForegroundColor Red }
}
function Assert-True($cond, [string]$what) { Assert-Equal 'True' ([bool]$cond) $what }
function New-TempDir {
    $d = Join-Path ([IO.Path]::GetTempPath()) ('wbf-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $d | Out-Null
    return $d
}
$sep = [IO.Path]::DirectorySeparatorChar

# --- CDX JSON parsing -------------------------------------------------------------------------------------------
$page1 = '[["urlkey","timestamp","original","mimetype","statuscode","digest","length"],' +
    '["com,camelotherald)/","20030101000000","http://www.camelotherald.com/","text/html","200","AAA","1234"],' +
    '["com,camelotherald)/servers.php","20030202000000","http://www.camelotherald.com/servers.php","text/html","200","BBB","99"],' +
    '[],["com%2Ccamelotherald%29%2Fz+20030303"]]'
$p = ConvertFrom-CdxJson $page1
Assert-Equal 2 $p.Rows.Count 'cdx: two data rows'
Assert-Equal 'com%2Ccamelotherald%29%2Fz+20030303' $p.ResumeKey 'cdx: resume key'
Assert-Equal 'http://www.camelotherald.com/servers.php' $p.Rows[1].original 'cdx: field by header name'
Assert-Equal '99' $p.Rows[1].length 'cdx: length field'

$single = '[["urlkey","timestamp","original","mimetype","statuscode","digest","length"],["a)/","2004","http://a/","text/html","200","D","1"]]'
$p = ConvertFrom-CdxJson $single
Assert-Equal 1 $p.Rows.Count 'cdx: single row is not unrolled'
Assert-Equal $null $p.ResumeKey 'cdx: last page has no resume key'
Assert-Equal 'http://a/' $p.Rows[0].original 'cdx: single row field'
Assert-Equal 0 (ConvertFrom-CdxJson '[]').Rows.Count 'cdx: empty result'
Assert-Equal 0 (ConvertFrom-CdxJson '').Rows.Count 'cdx: empty body'
$p = ConvertFrom-CdxJson '[["urlkey","timestamp","original"],[],["k1"]]'
Assert-Equal 0 $p.Rows.Count 'cdx: header and resume key only'
Assert-Equal 'k1' $p.ResumeKey 'cdx: resume key without rows'

# --- CDX query --------------------------------------------------------------------------------------------------
$jb = [pscustomobject]@{ from = '2002'; to = '2005'; mimetypes = @('text/html') }
$t = [pscustomobject]@{ url = 'camelotherald.com/guilds/'; matchType = 'prefix'; cdxFilters = @('urlkey:.*(merlin|percival).*') }
$q = Get-CdxQueryUrl 'https://web.archive.org' $jb $t 200 ''
Assert-True ($q.StartsWith('https://web.archive.org/cdx/search/cdx?url=camelotherald.com%2Fguilds%2F&matchType=prefix&from=2002&to=2005&output=json')) "cdx query start: $q"
Assert-True ($q -match '&filter=statuscode%3A200&') 'cdx query: status filter'
# .NET Framework 4.5+ and .NET Core both escape ( ) * in EscapeDataString; accept either form.
Assert-True ($q -match '&filter=mimetype%3A(\(|%28)text%2Fhtml(\)|%29)&') "cdx query: mimetype filter"
Assert-True ($q -match '&filter=urlkey%3A\.(\*|%2A)(\(|%28)merlin%7Cpercival(\)|%29)\.(\*|%2A)&') 'cdx query: extra filter escaped'
Assert-True ($q -match '&collapse=urlkey&limit=200&showResumeKey=true$') 'cdx query: collapse, limit, resume'
$q2 = Get-CdxQueryUrl 'https://web.archive.org' $jb $t 200 'com,x)/ 2003'
# Windows PowerShell 5.1 leaves ) unescaped, PowerShell 7 writes %29; the archive decodes both the same.
Assert-True ($q2 -match '&resumeKey=com%2Cx(\)|%29)%2F%202003$') "cdx query: resumeKey escaped ($q2)"
$te = [pscustomobject]@{ url = 'www.camelotherald.com/'; matchType = 'exact'; collapse = 'timestamp:6' }
$q3 = Get-CdxQueryUrl 'https://web.archive.org' $jb $te 100 ''
Assert-True (($q3 -notmatch 'matchType') -and ($q3 -match 'collapse=timestamp%3A6')) 'cdx query: exact and monthly collapse'
Assert-True ((Get-TargetKey $jb $t) -ne (Get-TargetKey $jb $te)) 'target key differs per query'

# --- Path hashing and URLs --------------------------------------------------------------------------------------
$rel = Get-SaveRelativePath 'http://www.CamelotHerald.com:80/guilds/x.php?id=1' '20030405060708' 'text/html'
$expectedHash = (Get-Sha256Hex (Get-Utf8Bytes '/guilds/x.php?id=1')).Substring(0, 16)
Assert-Equal ('www.camelotherald.com' + $sep + $expectedHash + '_20030405060708.html') $rel 'save path: host, hash, timestamp'
Assert-Equal 16 $expectedHash.Length 'save path: 16 hex hash'
Assert-True ((Get-SaveRelativePath 'http://a.com/x?id=1' '2003' 'text/html') -ne (Get-SaveRelativePath 'http://a.com/x?id=2' '2003' 'text/html')) 'save path: query changes the hash'
Assert-True ((Get-SaveRelativePath 'http://a.com/' '2003' 'text/plain').EndsWith('.txt')) 'save path: text/plain'
Assert-Equal ('a.com' + $sep + (Get-Sha256Hex (Get-Utf8Bytes '/')).Substring(0, 16) + '_2003.html') (Get-SaveRelativePath 'http://a.com' '2003' '') 'save path: bare host'
Assert-Equal 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855' (Get-Sha256Hex ([byte[]]@())) 'sha256 of nothing'
Assert-Equal 'https://web.archive.org/web/20030101000000id_/http://www.camelotherald.com/' (Get-IdUrl 'https://web.archive.org/' '20030101000000' 'http://www.camelotherald.com/') 'id_ url'
Assert-Equal 'camelotherald.com/guilds/a' (Get-NormalizedUrl 'HTTP://WWW.camelotherald.com:80/guilds/a') 'normalize url'

# --- Scope ------------------------------------------------------------------------------------------------------
function Row([string]$orig, [string]$ts = '20030101000000', [string]$urlkey = '') {
    if (-not $urlkey) { $urlkey = (Get-NormalizedUrl $orig) }
    return [pscustomobject]@{ original = $orig; timestamp = $ts; statuscode = '200'; urlkey = $urlkey; mimetype = 'text/html' }
}
$jb = [pscustomobject]@{ from = '2002'; to = '2005' }
$prefix = [pscustomobject]@{ url = 'camelotherald.com/guilds/'; matchType = 'prefix' }
Assert-True (Test-InScope (Row 'http://www.camelotherald.com/guilds/merlin/1.php') $jb $prefix) 'scope: prefix with www'
Assert-True (-not (Test-InScope (Row 'http://www.camelotherald.com/chars/1.php') $jb $prefix)) 'scope: other path'
Assert-True (-not (Test-InScope (Row 'http://evil.example/camelotherald.com/guilds/') $jb $prefix)) 'scope: other host'
Assert-True (-not (Test-InScope (Row 'http://www.camelotherald.com/guilds/1' '20060101000000') $jb $prefix)) 'scope: after to'
Assert-True (-not (Test-InScope (Row 'http://www.camelotherald.com/guilds/1' '20011231235959') $jb $prefix)) 'scope: before from'
Assert-True (Test-InScope (Row 'http://www.camelotherald.com/guilds/1' '20051231235959') $jb $prefix) 'scope: last day of to'
Assert-True (-not (Test-InScope (Row 'ftp://www.camelotherald.com/guilds/1') $jb $prefix)) 'scope: http only'
$hostOnly = [pscustomobject]@{ url = 'camelotherald.com'; matchType = 'prefix' }
Assert-True (-not (Test-InScope (Row 'http://camelotherald.community/x') $jb $hostOnly)) 'scope: host prefix does not leak to longer hosts'
$domain = [pscustomobject]@{ url = 'ign.com'; matchType = 'domain'; cdxFilters = @('urlkey:.*camelot.*') }
Assert-True (Test-InScope (Row 'http://vnboards.ign.com/Dark_Age_of_Camelot/b1/' '2003' 'com,ign,vnboards)/dark_age_of_camelot/b1') $jb $domain) 'scope: domain + filter'
Assert-True (-not (Test-InScope (Row 'http://vnboards.ign.com/board.asp?brd=1' '2003' 'com,ign,vnboards)/board.asp?brd=1') $jb $domain)) 'scope: filter applied locally'
Assert-True (-not (Test-InScope (Row 'http://notign.com/camelot' '2003') $jb $domain)) 'scope: domain suffix must be a dot boundary'
$exact = [pscustomobject]@{ url = 'www.camelotherald.com/'; matchType = 'exact' }
Assert-True (Test-InScope (Row 'http://camelotherald.com/') $jb $exact) 'scope: exact'
Assert-True (-not (Test-InScope (Row 'http://camelotherald.com/news') $jb $exact)) 'scope: exact rejects longer'
$neg = [pscustomobject]@{ url = 'camelotherald.com'; matchType = 'domain'; cdxFilters = @('!urlkey:.*/(chars|guilds)/.*') }
Assert-True (-not (Test-InScope (Row 'http://www.camelotherald.com/guilds/1' '2003' 'com,camelotherald)/guilds/1') $jb $neg)) 'scope: negated filter'
Assert-True (Test-InScope (Row 'http://www.camelotherald.com/servers.php' '2003' 'com,camelotherald)/servers.php') $jb $neg) 'scope: negated filter passes others'
$excl = [pscustomobject]@{ from = '2002'; to = '2005'; exclude = @('/(chars|guilds)/') }
Assert-True (-not (Test-InScope (Row 'http://www.camelotherald.com/chars/9') $excl $hostOnly)) 'scope: job exclude'
$bad = Row 'http://www.camelotherald.com/guilds/1'; $bad.statuscode = '302'
Assert-True (-not (Test-InScope $bad $jb $prefix)) 'scope: non-200 row'

# --- Pacing and back-off ----------------------------------------------------------------------------------------
$t0 = [DateTime]::new(2026, 1, 1, 0, 0, 0)
Assert-Equal 0 (Get-PaceDelay $null $t0 3) 'pace: first request'
Assert-Equal 2 (Get-PaceDelay $t0 $t0.AddSeconds(1) 3) 'pace: wait the rest of the interval'
Assert-Equal 0 (Get-PaceDelay $t0 $t0.AddSeconds(5) 3) 'pace: already waited'
$cfg = Get-DefaultConfig
$d = Get-RetryDecision 200 '' 1 $null $cfg; Assert-Equal 'ok' $d.Kind 'decision: 200'
$d = Get-RetryDecision 302 '' 1 $null $cfg; Assert-Equal 'redirect' $d.Kind 'decision: 302'
$d = Get-RetryDecision 404 '' 1 $null $cfg; Assert-Equal 'permanent' $d.Kind 'decision: 404'
$d = Get-RetryDecision 403 '' 1 $null $cfg; Assert-Equal 'permanent' $d.Kind 'decision: 403'
$d = Get-RetryDecision 503 '' 1 $null $cfg; Assert-Equal 'retry 10' ($d.Kind + ' ' + $d.Wait) 'decision: 503 first back-off'
$d = Get-RetryDecision 503 '' 3 $null $cfg; Assert-Equal 'retry 40' ($d.Kind + ' ' + $d.Wait) 'decision: exponential'
$d = Get-RetryDecision 0 'ConnectionClosed' 2 $null $cfg; Assert-Equal 'retry 20' ($d.Kind + ' ' + $d.Wait) 'decision: connection reset retries'
$d = Get-RetryDecision 429 '' 1 120 $cfg; Assert-Equal 'retry 120' ($d.Kind + ' ' + $d.Wait) 'decision: Retry-After longer than back-off wins'
$d = Get-RetryDecision 429 '' 3 5 $cfg; Assert-Equal 'retry 40' ($d.Kind + ' ' + $d.Wait) 'decision: back-off longer than Retry-After wins'
$d = Get-RetryDecision 429 '' 1 3600 $cfg; Assert-Equal 'stop' $d.Kind 'decision: huge Retry-After stops the run'
$d = Get-RetryDecision 500 '' 6 $null $cfg; Assert-Equal 'stop False' ($d.Kind + ' ' + $d.Throttled) 'decision: 5xx retries exhausted (not throttled)'
$d = Get-RetryDecision 429 '' 6 $null $cfg; Assert-Equal 'stop True' ($d.Kind + ' ' + $d.Throttled) 'decision: 429 retries exhausted (throttled)'
$small = Get-DefaultConfig; $small.backoffMaxSeconds = 30
$d = Get-RetryDecision 503 '' 5 $null $small; Assert-Equal 30 $d.Wait 'decision: back-off capped'
$now = [DateTime]::new(2026, 1, 1, 12, 0, 0, [DateTimeKind]::Utc)
Assert-Equal 7 (Get-RetryAfterSeconds '7' $now) 'retry-after: seconds'
Assert-Equal 30 (Get-RetryAfterSeconds 'Thu, 01 Jan 2026 12:00:30 GMT' $now) 'retry-after: http date'
Assert-Equal $null (Get-RetryAfterSeconds '' $now) 'retry-after: absent'
Assert-Equal $null (Get-RetryAfterSeconds 'soon' $now) 'retry-after: unreadable'

# Invoke-PoliteGet with a fake clock, sleeper and transport: pacing, retries, daily cap, Retry-After.
$tmp = New-TempDir
$script:StateDir = $tmp
$script:Config = Get-DefaultConfig
$script:Config.delaySeconds = 3
$script:LogPath = $null
$script:FakeNow = $t0
$script:Slept = New-Object System.Collections.Generic.List[double]
$script:Clock = { $script:FakeNow }
$script:Sleeper = { param([double]$s) $script:Slept.Add($s); $script:FakeNow = $script:FakeNow.AddSeconds($s) }
$script:Queue = New-Object System.Collections.Generic.Queue[object]
$script:Seen = New-Object System.Collections.Generic.List[string]
$script:Transport = { param($url, $cfg) $script:Seen.Add($url); $script:FakeNow = $script:FakeNow.AddSeconds(0.5); return $script:Queue.Dequeue() }
function Resp([int]$status, [hashtable]$headers = @{}, [string]$errorKind = '') {
    return [pscustomobject]@{ Status = $status; Bytes = (Get-Utf8Bytes 'x'); Headers = $headers; ErrorKind = $errorKind; TooLarge = $false }
}
$script:LastRequestStart = $null
$script:Queue.Enqueue((Resp 503)); $script:Queue.Enqueue((Resp 0 @{} 'ConnectionClosed')); $script:Queue.Enqueue((Resp 429 @{ 'retry-after' = '45' })); $script:Queue.Enqueue((Resp 200))
$r = Invoke-PoliteGet 'http://mock/a'
Assert-Equal 200 $r.Status 'polite get: ends with 200 after retries'
Assert-Equal 4 $script:Seen.Count 'polite get: four tries'
Assert-Equal '10,20,45' (($script:Slept | ForEach-Object { [string]$_ }) -join ',') 'polite get: back-off 10, 20, then Retry-After 45 (each longer than the pace, so no extra wait)'
Assert-Equal 4 (Get-DailyCount) 'polite get: every try counts against the daily cap'
$script:Slept.Clear()
$script:Queue.Enqueue((Resp 200))
[void](Invoke-PoliteGet 'http://mock/b')
Assert-Equal '2.5' (($script:Slept | ForEach-Object { [string]$_ }) -join ',') 'polite get: next request waits for the 3 s pace'
$script:Config.dailyRequestCap = 5
$stopped = ''
try { [void](Invoke-PoliteGet 'http://mock/c') } catch { $stopped = $_.Exception.Message }
Assert-True ($stopped -like 'STOP-RUN: the daily cap*') "polite get: daily cap stops the run ($stopped)"
$script:Config.dailyRequestCap = 1000
$script:Config.maxRetries = 1
$script:GiveUpsInARow = 0
$script:Queue.Clear(); $script:Queue.Enqueue((Resp 500)); $script:Queue.Enqueue((Resp 500))
$stopped = ''
try { [void](Invoke-PoliteGet 'http://mock/d') } catch { $stopped = $_.Exception.Message }
Assert-True ($stopped -like 'GIVE-UP: HTTP 500 after 2 tries') "polite get: a URL that keeps failing is given up ($stopped)"
$script:Queue.Enqueue((Resp 200))
[void](Invoke-PoliteGet 'http://mock/ok')
Assert-Equal 0 $script:GiveUpsInARow 'polite get: a success resets the give-up count'
$got = @()
foreach ($u in @('e', 'f', 'g')) {
    $script:Queue.Enqueue((Resp 0 @{} 'Timeout')); $script:Queue.Enqueue((Resp 0 @{} 'Timeout'))
    try { [void](Invoke-PoliteGet "http://mock/$u") } catch { $got += $_.Exception.Message.Split(':')[0] }
}
Assert-Equal 'GIVE-UP,GIVE-UP,STOP-RUN' ($got -join ',') 'polite get: three URLs given up in a row stop the run'
$script:GiveUpsInARow = 0
$script:Queue.Clear(); $script:Queue.Enqueue((Resp 429)); $script:Queue.Enqueue((Resp 429))
$stopped = ''
try { [void](Invoke-PoliteGet 'http://mock/h') } catch { $stopped = $_.Exception.Message }
Assert-True ($stopped -like 'STOP-RUN: the archive asks us to slow down*') "polite get: 429 after the retries stops the run ($stopped)"
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- Manifest resume --------------------------------------------------------------------------------------------
$tmp = New-TempDir
$mf = Join-Path $tmp 'manifest.jsonl'
$rel = 'a.com' + $sep + 'x_2003.html'
New-Item -ItemType Directory -Path (Join-Path $tmp 'a.com') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $tmp $rel), (Get-Utf8Bytes 'hello'))
$lines = @(
    '{"url":"http://a.com/x","timestamp":"2003","status":"error","bytes":0,"path":""}',
    ('{"url":"http://a.com/x","timestamp":"2003","status":"saved","bytes":5,"path":' + (ConvertTo-Json $rel) + '}'),
    '{"url":"http://a.com/y","timestamp":"2003","status":"error","bytes":0,"path":""}',
    '{"url":"http://a.com/z","timestamp":"2003","status":"missing","bytes":0,"path":""}',
    '{"url":"http://a.com/w","timestamp":"2003","status":"sav'
)
[IO.File]::WriteAllText($mf, ($lines -join "`n"))
$m = Read-Manifest $mf
Assert-Equal 3 $m.Count 'manifest: torn last line ignored, last line per key wins'
Assert-True (Test-ManifestDone $m['http://a.com/x 2003'] $tmp) 'manifest: saved file is done'
Assert-True (-not (Test-ManifestDone $m['http://a.com/y 2003'] $tmp)) 'manifest: error is retried'
Assert-True (Test-ManifestDone $m['http://a.com/z 2003'] $tmp) 'manifest: missing is not retried'
Assert-True (-not (Test-ManifestDone $m['http://a.com/w 2003'] $tmp)) 'manifest: unknown is fetched'
[IO.File]::WriteAllBytes((Join-Path $tmp $rel), (Get-Utf8Bytes 'hell'))
Assert-True (-not (Test-ManifestDone $m['http://a.com/x 2003'] $tmp)) 'manifest: truncated file is fetched again'
Remove-Item -LiteralPath (Join-Path $tmp $rel)
Assert-True (-not (Test-ManifestDone $m['http://a.com/x 2003'] $tmp)) 'manifest: deleted file is fetched again'
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- Job validation ---------------------------------------------------------------------------------------------
$tmp = New-TempDir
foreach ($jf in Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $here) 'jobs') -Filter '*.json') {
    $info = Read-Job $jf.FullName
    Assert-True ($info.Targets.Count -gt 0) ("starter job loads: " + $jf.Name)
    Assert-True (([string](Get-Prop $info.Def 'note' '')).Length -gt 20) ("starter job has a note: " + $jf.Name)
    foreach ($tg in $info.Targets) { Assert-True ($null -ne (Get-Prop $tg 'guess' $null)) ("target marks guess true/false: " + $jf.Name + ' ' + $tg.url) }
}
$badJob = Join-Path $tmp 'bad.json'
[IO.File]::WriteAllText($badJob, '{"name":"x","targets":[{"url":"a.com/*","matchType":"prefix"}]}')
$err = ''; try { [void](Read-Job $badJob) } catch { $err = $_.Exception.Message }
Assert-True ($err -match 'matchType instead') 'job: wildcard refused'
[IO.File]::WriteAllText($badJob, '{"name":"..\\x","targets":[{"url":"a.com"}]}')
$err = ''; try { [void](Read-Job $badJob) } catch { $err = $_.Exception.Message }
Assert-True ($err -match 'may only use') 'job: unsafe name refused'
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- Canonical exact matching and dedupeKey ---------------------------------------------------------------------
Assert-Equal 'camelotherald.com/chardisplay.php?c=12&s=merlin' (Get-CanonicalUrl 'http://www.camelotherald.com/chardisplay.php?s=Merlin&amp;c=12') 'canonical: params sorted, &amp; decoded'
Assert-Equal 'a.com/x' (Get-CanonicalUrl 'http://a.com/x/') 'canonical: trailing slash'
$ex = [pscustomobject]@{ url = 'forums.freddyshouse.com/showthread.php?s=abc&threadid=12'; matchType = 'exact' }
Assert-True (Test-InScope (Row 'http://forums.freddyshouse.com/showthread.php?threadid=12&s=abc') $jb $ex) 'scope: exact ignores query order (the index sorts it)'
Assert-True (-not (Test-InScope (Row 'http://forums.freddyshouse.com/showthread.php?threadid=123&s=abc') $jb $ex)) 'scope: exact still needs the same params'
$dj = [pscustomobject]@{ dedupeKey = '[?&](?:t|threadid|mid|topic)=(\d+)(?:&|$)' }
$k1 = Get-DedupeKey (Row 'http://forums.freddyshouse.com/showthread.php?s=aaa&threadid=12') $dj $ex
$k2 = Get-DedupeKey (Row 'http://www.forums.freddyshouse.com/showthread.php?threadid=12&s=bbb') $dj $ex
$k3 = Get-DedupeKey (Row 'http://forums.freddyshouse.com/showthread.php?threadid=123') $dj $ex
Assert-Equal 'forums.freddyshouse.com|12' $k1 'dedupe: host and thread id'
Assert-Equal $k1 $k2 'dedupe: another session id is the same thread'
Assert-True ($k1 -ne $k3) 'dedupe: another thread differs'
Assert-Equal $null (Get-DedupeKey (Row 'http://a.com/x') $jb $ex) 'dedupe: no dedupeKey'

# Invoke-Job with a fake transport: two URLs of one thread (different session ids) are fetched once.
$tmp = New-TempDir
$script:StateDir = $tmp; $script:LogPath = $null; $script:ArchiveBase = 'http://mock'
$script:Config = Get-DefaultConfig; $script:Config.delaySeconds = 0
$script:Clock = { [DateTime]::UtcNow }; $script:Sleeper = { param([double]$s) }
$script:GiveUpsInARow = 0
$cdxBody = '[["urlkey","timestamp","original","mimetype","statuscode","digest","length"],' +
    '["com,freddyshouse,forums)/showthread.php?s=aaa&threadid=12","20030101000000","http://forums.freddyshouse.com/showthread.php?s=aaa&threadid=12","text/html","200","D","1"],' +
    '["com,freddyshouse,forums)/showthread.php?s=bbb&threadid=12","20030202000000","http://forums.freddyshouse.com/showthread.php?s=bbb&threadid=12","text/html","200","D","1"],' +
    '["com,freddyshouse,forums)/showthread.php?s=ccc&threadid=13","20030303000000","http://forums.freddyshouse.com/showthread.php?s=ccc&threadid=13","text/html","200","D","1"]]'
$script:Seen = New-Object System.Collections.Generic.List[string]
$script:Transport = {
    param($url, $cfg)
    $script:Seen.Add($url)
    $body = '<html>page</html>'
    if ($url -like '*/cdx/*') { $body = $cdxBody }
    return [pscustomobject]@{ Status = 200; Bytes = (Get-Utf8Bytes $body); Headers = @{}; ErrorKind = ''; TooLarge = $false }
}
$djob = Join-Path $tmp 'd.json'
[IO.File]::WriteAllText($djob, '{"name":"dedupe","from":"2002","to":"2005","dedupeKey":"[?&](?:t|threadid)=(\\d+)(?:&|$)","targets":[{"url":"forums.freddyshouse.com/showthread.php","matchType":"prefix"}]}')
$st = [pscustomobject]@{ jobs = 0; indexRequests = 0; indexed = 0; fetched = 0; skipped = 0; failed = 0; copied = 0; bytes = 0 }
Invoke-Job (Read-Job $djob) $tmp $st
Assert-Equal 2 $st.fetched 'dedupe job: one capture per thread is fetched'
Assert-Equal 0 @($script:Seen | Where-Object { $_ -like '*s=bbb*' }).Count 'dedupe job: the second URL of thread 12 is never requested'
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- Patch versions named in a page ----------------------------------------------------------------------------
Assert-Equal '1.65;1.66b;1.62' (Get-PatchMentions 'Version 1.65 is live. Patch 1.66B on Pendragon. Since v1.62 ...') 'patch: versions in order, lower case'
Assert-Equal '1.70' (Get-PatchMentions '<b>1.70 Release Notes</b> and 1.70 patch') 'patch: number before "release", once'
Assert-Equal '' (Get-PatchMentions 'costs 1.65 gold, version 2.1, 11.65 patch, v1.650') 'patch: prices and other numbers ignored'
Assert-Equal '1.41;1.42;1.43' (Get-PatchMentions 'patch 1.41 patch 1.42 patch 1.43 patch 1.44') 'patch: at most three'

# --- Starter jobs: the re-planned set (lore, vocab, builds, specs; no character or roster harvesting) -----------
$jobsDir = Join-Path (Split-Path -Parent $here) 'jobs'
$names = @(Get-ChildItem -LiteralPath $jobsDir -Filter '*.json' | Sort-Object Name | ForEach-Object { $_.BaseName }) -join ','
Assert-Equal '1-site-map,2-patch-notes,3-builds-specs,4-vocab,5-lore,6-mechanics,7-realms-classes,8-spells,9-herald-articles,a-raid-sizes' $names 'jobs: run order map, patch notes, builds, vocab, lore, mechanics, realms probe, spells, articles'
$allTargets = @()
foreach ($jf in Get-ChildItem -LiteralPath $jobsDir -Filter '*.json') { $allTargets += @((Read-Job $jf.FullName).Targets) }
Assert-Equal 0 @($allTargets | Where-Object { $_.url -match 'chardisplay|/chars/' -and -not (@(Get-Prop $_ 'cdxFilters' @()) -match '^!') }).Count 'jobs: no character pages fetched'
Assert-Equal 0 @($allTargets | Where-Object { $_.matchType -eq 'domain' -and $_.url -like '*camelotherald*' }).Count 'jobs: no Herald domain-wide scans (503/504)'
$map = Read-Job (Join-Path $jobsDir '1-site-map.json')
Assert-True ([bool]$map.Def.indexOnly) 'map: index only'
$letters = @($map.Targets | Where-Object { $_.url -match '^camelotherald\.com/[a-z]$' } | ForEach-Object { $_.url.Substring($_.url.Length - 1) }) -join ''
Assert-Equal 'abcdefghijklmnopqrstuvwxyz' $letters 'map: one Herald prefix range per letter a-z'
$tc = $map.Targets | Where-Object { $_.url -eq 'camelotherald.com/c' }
$tg = $map.Targets | Where-Object { $_.url -eq 'camelotherald.com/g' }
$hr = 'http://www.camelotherald.com/'
Assert-True (-not (Test-InScope (Row ($hr + 'chardisplay.php?s=Merlin&c=5') '2003' 'com,camelotherald)/chardisplay.php?c=5&s=merlin') $map.Def $tc)) 'map: chardisplay.php left out'
Assert-True (-not (Test-InScope (Row ($hr + 'calendar/2003/05.php') '2003' 'com,camelotherald)/calendar/2003/05.php') $map.Def $tc)) 'map: /calendar/ left out'
Assert-True (Test-InScope (Row ($hr + 'catacombs_beta.php') '2003' 'com,camelotherald)/catacombs_beta.php') $map.Def $tc) 'map: other c pages kept'
Assert-True (Test-InScope (Row ($hr + 'guilds/x.php') '2003' 'com,camelotherald)/guilds/x.php') $map.Def $tg) 'map: g range reaches /guilds/'
Assert-True (-not (Test-InScope (Row ($hr + 'guilds/x.php') '2003' 'com,camelotherald)/guilds/x.php') $map.Def $tc)) 'map: ranges do not overlap'
Assert-True ($null -ne ($map.Targets | Where-Object { $_.url -eq 'darkageofcamelot.com/' })) 'map: the official site is mapped too'
$pn = Read-Job (Join-Path $jobsDir '2-patch-notes.json')
$arch = $pn.Targets | Where-Object { $_.url -eq 'camelotherald.com/archives.php' }
Assert-True (Test-InScope (Row ($hr + 'archives.php?t=game&sortby=date') '2003' 'com,camelotherald)/archives.php?sortby=date&t=game') $pn.Def $arch) 'patch-notes: game archive'
Assert-True (-not (Test-InScope (Row ($hr + 'archives.php?t=community') '2003' 'com,camelotherald)/archives.php?t=community') $pn.Def $arch)) 'patch-notes: not the community archive'
Assert-True ($null -ne ($pn.Targets | Where-Object { $_.url -eq 'camelotherald.com/article.php' })) 'patch-notes: article.php'
$bs = Read-Job (Join-Path $jobsDir '3-builds-specs.json')
Assert-True ($null -ne ($bs.Targets | Where-Object { $_.url -eq 'camelotherald.com/newfrontiers/' -and -not $_.guess })) 'builds: confirmed newfrontiers (realm abilities)'
$guides = @($bs.Targets | Where-Object { $_.url -like '*_Guide.html' })
Assert-Equal 39 $guides.Count 'builds: one guide lookup per classic and SI class'
Assert-Equal 0 @($guides | Where-Object { -not $_.guess }).Count 'builds: guide URLs marked as guesses'
Assert-True ((Get-CdxQueryUrl 'https://web.archive.org' $bs.Def $guides[0] 50 '') -match 'collapse=timestamp%3A4') 'builds: guides once per year'
$sum = 0; foreach ($t in $bs.Targets) { $sum += [int]$t.maxCaptures }
Assert-True ([int]$bs.Def.maxCaptures -ge $sum) "builds: the job cap ($($bs.Def.maxCaptures)) leaves room for every target ($sum)"
$vb = $bs.Targets | Where-Object { $_.url -eq 'vnboards.ign.com/dark_age_of_camelot' }
Assert-True (Test-InScope (Row 'http://vnboards.ign.com/Dark_Age_of_Camelot_Paladin/b20057/' '2003' 'com,ign,vnboards)/dark_age_of_camelot_paladin/b20057') $bs.Def $vb) 'builds: a VN Boards class board'
Assert-True (-not (Test-InScope (Row 'http://vnboards.ign.com/Dark_Age_of_Camelot_Trading/b20001/' '2003' 'com,ign,vnboards)/dark_age_of_camelot_trading/b20001') $bs.Def $vb)) 'builds: not the trade board'
$vo = Read-Job (Join-Path $jobsDir '4-vocab.json')
$vvb = $vo.Targets | Where-Object { $_.url -eq 'vnboards.ign.com/dark_age_of_camelot' }
Assert-True (Test-InScope (Row 'http://vnboards.ign.com/Dark_Age_of_Camelot_Trading/b20001/' '2003' 'com,ign,vnboards)/dark_age_of_camelot_trading/b20001') $vo.Def $vvb) 'vocab: the trade board'
$vg = $vo.Targets | Where-Object { $_.url -like '*/guilds/*' }
Assert-True ([bool]$vg.guess -and [int]$vg.maxCaptures -le 10) 'vocab: guild pages only a tiny guessed wording sample'
$lo = Read-Job (Join-Path $jobsDir '5-lore.json')
$q = $lo.Targets | Where-Object { $_.url -eq 'camelot.allakhazam.com/db/quests.html' }
Assert-True (Test-InScope (Row 'http://camelot.allakhazam.com/db/quests.html?cquest=459' '2004') $lo.Def $q) 'lore: an Allakhazam quest page'
foreach ($jn in @('2-patch-notes', '3-builds-specs', '4-vocab', '5-lore')) {
    $j = Read-Job (Join-Path $jobsDir ($jn + '.json'))
    $sum = 0; foreach ($t in $j.Targets) { $sum += [int]$t.maxCaptures }
    Assert-True ([int]$j.Def.maxCaptures -ge $sum) "$($jn): the job cap ($($j.Def.maxCaptures)) leaves room for every target ($sum)"
}

# --- pick: first, latest, latest-per-year -----------------------------------------------------------------------
$pr = @(
    (Row 'http://www.camelotherald.com/realmabilities/paladin.html' '20020601000000'),
    (Row 'http://camelotherald.com/realmabilities/paladin.html' '20021101000000'),
    (Row 'http://www.camelotherald.com/realmabilities/paladin.html' '20030301000000'),
    (Row 'http://www.camelotherald.com/realmabilities/cleric.html' '20020701000000')
)
$pt = [pscustomobject]@{ url = 'camelotherald.com/realmabilities/'; matchType = 'prefix' }
Assert-Equal 4 (Select-Captures $pr $jb $pt).Count 'pick: first keeps every row'
$pt | Add-Member pick 'latest'
$sel = Select-Captures $pr $jb $pt
Assert-Equal '20030301000000,20020701000000' (@($sel | ForEach-Object { $_.timestamp }) -join ',') 'pick latest: the latest of each page, page order kept'
$pt.pick = 'latest-per-year'
$sel = Select-Captures $pr $jb $pt
Assert-Equal '20021101000000,20030301000000,20020701000000' (@($sel | ForEach-Object { $_.timestamp }) -join ',') 'pick latest-per-year: one per page per year'
$pt.pick = 'latest'
$pt | Add-Member dedupeKey '/more/(\d+)\.(?:php|shtml)$'
$mr = @((Row 'http://www.camelotherald.com/more/10.php' '20020616000000'), (Row 'http://www.camelotherald.com/more/10.shtml' '20040305000000'), (Row 'http://www.camelotherald.com/more/100.php' '20020201000000'))
Assert-Equal 'http://www.camelotherald.com/more/10.shtml,http://www.camelotherald.com/more/100.php' (@(Select-Captures $mr $jb $pt | ForEach-Object { $_.original }) -join ',') 'pick latest + dedupeKey: one article per id'
$tmp = New-TempDir
$badJob = Join-Path $tmp 'bad.json'
[IO.File]::WriteAllText($badJob, '{"name":"x","targets":[{"url":"a.com","pick":"newest"}]}')
$err = ''; try { [void](Read-Job $badJob) } catch { $err = $_.Exception.Message }
Assert-True ($err -match "pick 'newest'") 'job: unknown pick refused'
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- Jobs 6-9 from the site map ---------------------------------------------------------------------------------
function Get-Target($job, [string]$url) { return @($job.Targets | Where-Object { $_.url -eq $url })[0] }
function Test-Takes($job, [string]$url, [string]$orig) { return (Test-InScope (Row $orig '20040101000000') $job.Def (Get-Target $job $url)) }
$m6 = Read-Job (Join-Path $jobsDir '6-mechanics.json')
$gb = Get-Target $m6 'www.camelotherald.com/grabbag.php'
Assert-Equal 'exact first' ($gb.matchType + ' ' + (Get-Prop $gb 'pick' 'first')) 'mechanics: the Grab Bag page, every version'
$gq = Get-CdxQueryUrl 'https://web.archive.org' $m6.Def $gb 500 ''
Assert-True ($gq -match '&collapse=digest&') "mechanics: Grab Bag collapses identical versions only ($gq)"
$gbRows = @((Row 'http://www.camelotherald.com/grabbag.php' '20040803030132'), (Row 'http://www.camelotherald.com/grabbag.php' '20040810030132'))
Assert-Equal 2 (Select-Captures $gbRows $m6.Def $gb).Count 'mechanics: two Grab Bag versions both kept'
Assert-True (Test-Takes $m6 'camelotherald.com/documentation/' 'http://www.camelotherald.com:80/documentation/respec-costs.php') 'mechanics: respec costs'
Assert-True (Test-Takes $m6 'camelotherald.com/teamleads/' 'http://www.camelotherald.com:80/teamleads/tl_reports.php?mode=&amp;id=&amp;tlid=') 'mechanics: team lead reports'
Assert-True (Test-Takes $m6 'camelotherald.com/masterlevels/' 'http://www.camelotherald.com/masterlevels/ma.php?ml=Banelord') 'mechanics: master levels'
$ra = Get-Target $m6 'camelotherald.com/realmabilities/'
Assert-Equal 'latest-per-year' $ra.pick 'mechanics: realm abilities once per patch year'
Assert-True (Test-Takes $m6 'camelotherald.com/newfrontiers/' 'http://www.camelotherald.com/newfrontiers/realmabilities/paladin.php') 'mechanics: New Frontiers realm abilities'
Assert-True (-not (Test-Takes $m6 'camelotherald.com/newfrontiers/' 'http://www.camelotherald.com/newfrontiers/media/screenshots/12.html')) 'mechanics: no screenshots'
Assert-True (-not (Test-Takes $m6 'camelotherald.com/newfrontiers/' 'http://www.camelotherald.com/newfrontiers/allserverstats.php')) 'mechanics: no server statistics'
Assert-True (Test-Takes $m6 'classesofcamelot.com/Classes.asp' 'http://www.classesofcamelot.com/Classes.asp?class=Armsman') 'mechanics: Classes of Camelot class page'
Assert-True (Test-Takes $m6 'classesofcamelot.com/faq.asp' 'http://www.classesofcamelot.com/faq.asp?mode=view&cat=10') 'mechanics: Classes of Camelot FAQ'
Assert-True (-not (Test-Takes $m6 'darkageofcamelot.com/roundtable/' 'http://www.darkageofcamelot.com/roundtable/gallery/1_2.html')) 'mechanics: no Round Table photo gallery'
Assert-Equal 0 @($allTargets + $m6.Targets | Where-Object { $_.url -match '/(hdirectory|japan)' }).Count 'jobs: /hdirectory and /japan skipped'
$r7 = Read-Job (Join-Path $jobsDir '7-realms-classes.json')
Assert-True ($r7.Targets.Count -le 8 -and [int]$r7.Def.maxCaptures -le 8) 'realms: a probe of at most 8 pages'
Assert-Equal 0 @($r7.Targets | Where-Object { $_.matchType -ne 'exact' }).Count 'realms: exact pages only'
Assert-Equal 0 @($r7.Targets | Where-Object { $_.url -match '/realms/(?!Merlin/|$)' }).Count 'realms: one server only'
$s8 = Read-Job (Join-Path $jobsDir '8-spells.json')
Assert-Equal 'camelotherald.com/spells/line.php' $s8.Targets[0].url 'spells: spell lines first'
$sp = Get-Target $s8 'camelotherald.com/spells/spell.php'
$spRows = @((Row 'http://www.camelotherald.com/spells/spell.php?s=1462&amp' '20040812205002'), (Row 'http://www.camelotherald.com/spells/spell.php?s=1462&level=20' '20040901000000'), (Row 'http://www.camelotherald.com/spells/spell.php?s=520&level=5' '20040801000000'))
$spSel = Select-Captures $spRows $s8.Def $sp
Assert-Equal '20040901000000,20040801000000' (@($spSel | ForEach-Object { $_.timestamp }) -join ',') 'spells: one page per spell id (the latest), &amp; variants merged'
Assert-True ([int]$s8.Def.maxCaptures -ge 2950) 'spells: room for every spell and line page'
$a9 = Read-Job (Join-Path $jobsDir '9-herald-articles.json')
Assert-True ([bool]$a9.Targets[0].guess) 'articles: content marked a guess'

# --- pack-results.ps1 -------------------------------------------------------------------------------------------
$exe = (Get-Process -Id $PID).Path
$packer = Join-Path (Split-Path -Parent $here) 'pack-results.ps1'
$tmp = New-TempDir
$pj = Join-Path (Join-Path $tmp 'out') '2-patch-notes'
foreach ($d in @('www.camelotherald.com', 'index')) { New-Item -ItemType Directory -Path (Join-Path $pj $d) -Force | Out-Null }
foreach ($d in @('_logs', '_state', '3-empty')) { New-Item -ItemType Directory -Path (Join-Path (Join-Path $tmp 'out') $d) -Force | Out-Null }
[IO.File]::WriteAllText((Join-Path $pj 'index.csv'), 'saved_path,timestamp')
[IO.File]::WriteAllText((Join-Path $pj 'manifest.jsonl'), '{}')
[IO.File]::WriteAllText((Join-Path (Join-Path $pj 'www.camelotherald.com') 'aaaa_2003.html'), '<html>1.65</html>')
[IO.File]::WriteAllText((Join-Path (Join-Path $pj 'www.camelotherald.com') 'bbbb_2004.html.part'), 'half')
[IO.File]::WriteAllText((Join-Path (Join-Path $pj 'index') 'target1-x.jsonl'), '{}')
[IO.File]::WriteAllText((Join-Path (Join-Path (Join-Path $tmp 'out') '_logs') 'run.log'), 'log')
$pk1 = & $exe -NoProfile -ExecutionPolicy Bypass -File $packer -Folder $tmp 2>&1 | Out-String
Assert-Equal 0 $LASTEXITCODE "pack: exit code`n$pk1"
$zips = @(Get-ChildItem -LiteralPath (Join-Path (Join-Path $tmp 'out') '_packs') -Filter '*.zip')
Assert-Equal ('2-patch-notes-' + (Get-Date).ToString('yyyyMMdd') + '.zip') (@($zips | ForEach-Object { $_.Name }) -join ',') "pack: one zip per job with results, named <job>-<yyyymmdd>.zip`n$pk1"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$za = [System.IO.Compression.ZipFile]::OpenRead($zips[0].FullName)
try {
    $raw = @($za.Entries | Where-Object { $_.FullName -like '*aaaa_2003.html' } | ForEach-Object { $_.FullName })
    $entries = @($za.Entries | Where-Object { $_.Length -gt 0 -or -not $_.FullName.EndsWith('/') } | ForEach-Object { $_.FullName.Replace('\', '/') } | Sort-Object)
} finally { $za.Dispose() }
Assert-Equal 'index.csv,manifest.jsonl,www.camelotherald.com/aaaa_2003.html' (@($entries | Where-Object { -not $_.EndsWith('/') }) -join ',') 'pack: pages, index.csv and manifest; no index state, .part files or logs'
Assert-Equal 'www.camelotherald.com/aaaa_2003.html' ($raw -join ',') "pack: entry names use '/' (also on Windows PowerShell 5.1)"
$pk2 = & $exe -NoProfile -ExecutionPolicy Bypass -File $packer -Folder $tmp 2>&1 | Out-String
Assert-True ($pk2 -match '2-patch-notes: unchanged since its last pack, skipped' -and $pk2 -match '0 packed, 1 unchanged') "pack: unchanged job skipped`n$pk2"
Start-Sleep -Milliseconds 50
[IO.File]::WriteAllText((Join-Path (Join-Path $pj 'www.camelotherald.com') 'cccc_2005.html'), '<html>new</html>')
[IO.File]::WriteAllText((Join-Path (Join-Path $pj 'index') 'target1-x.jsonl'), '{"changed":1}')
$pk3 = & $exe -NoProfile -ExecutionPolicy Bypass -File $packer -Folder $tmp 2>&1 | Out-String
Assert-True ($pk3 -match '1 packed') "pack: changed job packed again`n$pk3"
$za = [System.IO.Compression.ZipFile]::OpenRead($zips[0].FullName)
try { $n3 = @($za.Entries | Where-Object { $_.FullName.Replace('\', '/') -like '*cccc_2005.html' }).Count } finally { $za.Dispose() }
Assert-Equal 1 $n3 'pack: the new page is in the repacked zip'
Remove-Item -LiteralPath (Join-Path (Join-Path $pj 'index') 'target1-x.jsonl')
$pk4 = & $exe -NoProfile -ExecutionPolicy Bypass -File $packer -Folder $tmp 2>&1 | Out-String
Assert-True ($pk4 -match '0 packed, 1 unchanged') "pack: index state changes alone don't repack`n$pk4"
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- make-thread-job.ps1 ----------------------------------------------------------------------------------------
$exe = (Get-Process -Id $PID).Path
$maker = Join-Path (Split-Path -Parent $here) 'make-thread-job.ps1'
$tmp = New-TempDir
$src = Join-Path (Join-Path $tmp 'out') 'a-raid-sizes'
New-Item -ItemType Directory -Path $src -Force | Out-Null
$sid = 's=0123456789abcdef0123456789abcdef'
$vb = @"
<html><body><table>
<tr><td><a href="showthread.php?$sid&amp;threadid=1001">Midgard <b>dragon</b> raid - how many?</a>
 <a href="showthread.php?$sid&amp;threadid=1001&amp;pagenumber=2">2</a>
 <a href="showthread.php?$sid&amp;goto=lastpost&amp;threadid=1001">Last Page</a></td></tr>
<tr><td><a href="showthread.php?$sid&amp;threadid=1002">Best spec for a Thane?</a></td></tr>
<tr><td><a href="showthread.php?threadid=1003">DF tonight</a></td></tr>
<tr><td><a href="showthread.php?threadid=1004">pdf of the manual, df</a></td></tr>
<tr><td><a href="http://www.other.com/showthread.php?t=9">Dragon raid elsewhere</a></td></tr>
<tr><td><a href='showthread.php?t=1005'>Gjalpinulva &amp; Legion</a></td></tr>
<tr><td><a href="showthread.php?p=777">Raid post link</a></td></tr>
</table></body></html>
"@
$alla = @"
<a href="/forum.html?forum=13&mid=2001">Caer Sidi epic raid sizes</a>
<a href="/forum.html?forum=13&mid=2002">Crafting prices</a>
<a href="forum.html?forum=13&amp;mid=2001">Caer Sidi epic raid sizes</a>
"@
[IO.File]::WriteAllText((Join-Path $src 'vb.html'), $vb)
[IO.File]::WriteAllText((Join-Path $src 'al.html'), $alla)
$mlines = @(
    '{"url":"http://forums.freddyshouse.com/forumdisplay.php?f=5","timestamp":"20030101000000","status":"saved","bytes":0,"path":"vb.html"}',
    '{"url":"http://camelot.allakhazam.com/forum.html?forum=13","timestamp":"20040101000000","status":"saved","bytes":0,"path":"al.html"}',
    '{"url":"http://forums.freddyshouse.com/forumdisplay.php?f=6","timestamp":"20030101000000","status":"missing","bytes":0,"path":""}'
)
[IO.File]::WriteAllText((Join-Path $src 'manifest.jsonl'), ($mlines -join "`n"))
$mk1 = & $exe -NoProfile -ExecutionPolicy Bypass -File $maker -Folder $tmp 2>&1 | Out-String
Assert-Equal 0 $LASTEXITCODE "make-thread-job: exit code`n$mk1"
$genPath = Join-Path (Join-Path $tmp 'jobs') 'a2-raid-threads.json'
$gen = Read-Job $genPath
$ids = @($gen.Def.threads | ForEach-Object { $_.id }) -join ','
Assert-Equal '2001,1001,1003,1005' $ids 'make-thread-job: raid threads only (DF in capitals, other hosts and post links left out), one per thread'
Assert-Equal 4 $gen.Targets.Count 'make-thread-job: one exact target per thread'
Assert-Equal 0 @($gen.Targets | Where-Object { $_.matchType -ne 'exact' }).Count 'make-thread-job: all exact on the first run'
$t1001 = $gen.Targets | Where-Object { $_.url -like '*threadid=1001' }
Assert-Equal "forums.freddyshouse.com/showthread.php?$sid&threadid=1001" $t1001.url 'make-thread-job: the thread link as listed (not page 2, not last post)'
Assert-True ($t1001.note -like '*"Midgard dragon raid - how many?"*') "make-thread-job: title in the note ($($t1001.note))"
Assert-Equal 'camelot.allakhazam.com/forum.html?forum=13&mid=2001' ($gen.Targets | Where-Object { $_.url -like '*mid=2001' }).url 'make-thread-job: Allakhazam link resolved'
Assert-True (Test-InScope (Row "http://forums.freddyshouse.com/showthread.php?threadid=1001&$sid") $gen.Def $t1001) 'make-thread-job: exact target accepts its capture'

# Second run after a fetch run found nothing for thread 1003 by its exact link: it moves to an id-filter target.
$t1003 = $gen.Targets | Where-Object { $_.url -like '*threadid=1003' }
$idxDir = Join-Path (Join-Path (Join-Path $tmp 'out') 'a2-raid-threads') 'index'
New-Item -ItemType Directory -Path $idxDir -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $idxDir ('target3-' + (Get-TargetKey $gen.Def $t1003) + '.state.json')), '{"done":true,"pages":1,"resumeKey":"","rows":0}')
$t1005 = $gen.Targets | Where-Object { $_.url -like '*t=1005' }
[IO.File]::WriteAllText((Join-Path $idxDir ('target4-' + (Get-TargetKey $gen.Def $t1005) + '.state.json')), '{"done":true,"pages":1,"resumeKey":"","rows":1}')
$mk2 = & $exe -NoProfile -ExecutionPolicy Bypass -File $maker -Folder $tmp 2>&1 | Out-String
Assert-Equal 0 $LASTEXITCODE "make-thread-job: second run exit code`n$mk2"
$gen2 = Read-Job $genPath
$filt = @($gen2.Targets | Where-Object { $_.matchType -eq 'prefix' })
Assert-Equal 1 $filt.Count 'make-thread-job: one id-filter target'
Assert-Equal 3 @($gen2.Targets | Where-Object { $_.matchType -eq 'exact' }).Count 'make-thread-job: found threads stay exact'
Assert-Equal 'forums.freddyshouse.com/showthread.php' $filt[0].url 'make-thread-job: filter target on the forum''s showthread.php'
Assert-Equal 'id-filter' ($gen2.Def.threads | Where-Object { $_.id -eq '1003' }).lookup 'make-thread-job: thread list says how it is looked up'
$fr = 'http://forums.freddyshouse.com/showthread.php?'
Assert-True (Test-InScope (Row ($fr + 's=ffff&threadid=1003') '2003' 'com,freddyshouse,forums)/showthread.php?s=ffff&threadid=1003') $gen2.Def $filt[0]) 'make-thread-job: filter finds the thread under another session id'
Assert-True (-not (Test-InScope (Row ($fr + 'threadid=10030') '2003' 'com,freddyshouse,forums)/showthread.php?threadid=10030') $gen2.Def $filt[0])) 'make-thread-job: filter matches the whole id'
Assert-True (-not (Test-InScope (Row ($fr + 'pagenumber=2&threadid=1003') '2003' 'com,freddyshouse,forums)/showthread.php?pagenumber=2&threadid=1003') $gen2.Def $filt[0])) 'make-thread-job: filter skips page 2'
Assert-True (-not (Test-InScope (Row ($fr + 'goto=lastpost&threadid=1003') '2003' 'com,freddyshouse,forums)/showthread.php?goto=lastpost&threadid=1003') $gen2.Def $filt[0])) 'make-thread-job: filter skips last-post links'

# No raid thread: no job is written.
[IO.File]::WriteAllText((Join-Path $src 'manifest.jsonl'), $mlines[2])
$mk3 = & $exe -NoProfile -ExecutionPolicy Bypass -File $maker -Folder $tmp 2>&1 | Out-String
Assert-Equal 1 $LASTEXITCODE "make-thread-job: nothing found exits 1`n$mk3"
Assert-True (-not (Test-Path -LiteralPath $genPath)) 'make-thread-job: no empty job left behind'

# The builds and talk presets, reading listings from 4-vocab as well.
$vsrc = Join-Path (Join-Path $tmp 'out') '4-vocab'
New-Item -ItemType Directory -Path $vsrc -Force | Out-Null
$talk = @"
<a href="showthread.php?t=3001">50 Paladin spec for 1.65?</a>
<a href="showthread.php?t=3002">RAs for a RR5 Infil</a>
<a href="showthread.php?t=3003">WTS SC'd Stalwart armor</a>
<a href="showthread.php?t=3004">&lt;Avalon&gt; recruiting casters</a>
<a href="showthread.php?t=3005">INC Emain, mile gate</a>
<a href="showthread.php?t=3006">Funny story from last night</a>
<a href="showthread.php?t=3007">The ra of the sun</a>
"@
[IO.File]::WriteAllText((Join-Path $vsrc 'f.html'), $talk)
[IO.File]::WriteAllText((Join-Path $vsrc 'manifest.jsonl'), '{"url":"http://forums.freddyshouse.com/forumdisplay.php?f=9","timestamp":"20040101000000","status":"saved","bytes":0,"path":"f.html"}')
$mk4 = & $exe -NoProfile -ExecutionPolicy Bypass -File $maker -Folder $tmp -Preset builds 2>&1 | Out-String
Assert-Equal 0 $LASTEXITCODE "make-thread-job builds: exit code`n$mk4"
$bj = Read-Job (Join-Path (Join-Path $tmp 'jobs') 'a3-build-threads.json')
Assert-Equal '3001,3002,3003' (@($bj.Def.threads | ForEach-Object { $_.id }) -join ',') 'make-thread-job builds: spec, RA/RR and SC titles (lower-case "ra" ignored)'
$mk5 = & $exe -NoProfile -ExecutionPolicy Bypass -File $maker -Folder $tmp -Preset talk 2>&1 | Out-String
Assert-Equal 0 $LASTEXITCODE "make-thread-job talk: exit code`n$mk5"
$tj = Read-Job (Join-Path (Join-Path $tmp 'jobs') 'a4-talk-threads.json')
Assert-Equal '3003,3004,3005' (@($tj.Def.threads | ForEach-Object { $_.id }) -join ',') 'make-thread-job talk: WTS, recruiting and INC/Emain titles'
Assert-Equal '&lt;Avalon&gt; recruiting casters' ([System.Net.WebUtility]::HtmlEncode(($tj.Def.threads | Where-Object { $_.id -eq '3004' }).title)) 'make-thread-job: titles HTML-decoded'
Remove-Item -LiteralPath $tmp -Recurse -Force

# --- End to end against tests\mock_archive.py -------------------------------------------------------------------
function Find-Python {
    if ($Python) { return $Python }
    foreach ($c in @('python3', 'python', 'py')) { $cmd = Get-Command $c -ErrorAction SilentlyContinue; if ($cmd) { return $cmd.Source } }
    return $null
}
$py = Find-Python
if (-not $py) {
    if ($RequireE2E) { $script:Failures++; Write-Host 'FAIL e2e: Python 3 not found' -ForegroundColor Red }
    else { Write-Host 'SKIP e2e: Python 3 not found' }
} else {
    $work = New-TempDir
    New-Item -ItemType Directory -Path (Join-Path $work 'jobs') | Out-Null
    $e2eJob = @'
{
    "name": "e2e",
    "note": "end-to-end test job",
    "from": "2002", "to": "2005",
    "targets": [ { "url": "camelotherald.com/guilds/", "matchType": "prefix", "guess": false, "cdxPageSize": 3, "cdxMaxPages": 5 } ]
}
'@
    [IO.File]::WriteAllText((Join-Path (Join-Path $work 'jobs') 'e2e.json'), $e2eJob)
    [IO.File]::WriteAllText((Join-Path $work 'wayback-fetch.config.json'), '{"backoffBaseSeconds": 1, "backoffMaxSeconds": 4, "maxRetries": 2}')
    $reqLog = Join-Path $work 'requests.jsonl'
    $portFile = Join-Path $work 'port.txt'
    $mock = Join-Path $here 'mock_archive.py'
    $startArgs = @{ FilePath = $py; ArgumentList = @(('"' + $mock + '"'), '--port-file', ('"' + $portFile + '"'), '--log', ('"' + $reqLog + '"')); PassThru = $true }
    if ($PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows) { $startArgs['WindowStyle'] = 'Hidden' }
    $proc = Start-Process @startArgs
    try {
        for ($i = 0; $i -lt 100 -and -not (Test-Path -LiteralPath $portFile); $i++) { Start-Sleep -Milliseconds 100 }
        $port = ([IO.File]::ReadAllText($portFile)).Trim()
        $base = "http://127.0.0.1:$port"
        $exe = (Get-Process -Id $PID).Path
        $run1 = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -ArchiveBase $base 2>&1 | Out-String
        $code1 = $LASTEXITCODE
        Assert-Equal 0 $code1 "e2e run 1 exit code`n$run1"
        $jobDir = Join-Path (Join-Path $work 'out') 'e2e'
        $m1 = Read-Manifest (Join-Path $jobDir 'manifest.jsonl')
        $saved = @($m1.Values | Where-Object { $_.status -eq 'saved' })
        Assert-Equal 4 $saved.Count 'e2e: four pages saved (one after a 503, one after a redirect)'
        Assert-Equal 'missing' $m1['http://www.camelotherald.com/guilds/gone.php 20030505000000'].status 'e2e: 404 recorded as missing'
        Assert-Equal 'out-of-scope' $m1['http://www.camelotherald.com/guilds/jump.php 20030606000000'].status 'e2e: redirect to another site refused'
        Assert-True (-not $m1.ContainsKey('http://www.example.com/guilds/x 20030101000000')) 'e2e: index row for another host never fetched'
        Assert-True (-not $m1.ContainsKey('http://www.camelotherald.com/guilds/old.php 19990101000000')) 'e2e: index row outside the years never fetched'
        foreach ($s in $saved) {
            $full = Join-Path $jobDir $s.path
            Assert-True (Test-Path -LiteralPath $full) ('e2e: file exists ' + $s.path)
            Assert-Equal $s.sha256 (Get-Sha256Hex ([IO.File]::ReadAllBytes($full))) ('e2e: sha256 matches ' + $s.path)
            Assert-True ($s.path -match ('^www\.camelotherald\.com\' + $sep + '[0-9a-f]{16}_\d{14}\.html$')) ('e2e: path shape ' + $s.path)
        }
        $moved = $m1['http://www.camelotherald.com/guilds/moved.php 20030404000000']
        Assert-Equal '20030405000000' $moved.capture 'e2e: same-page redirect followed to the other capture time'
        $csv = @(Import-Csv -LiteralPath (Join-Path $jobDir 'index.csv'))
        Assert-Equal 4 $csv.Count 'e2e: index.csv lists the saved pages'
        $requests = @([IO.File]::ReadAllLines($reqLog) | Where-Object { $_.Trim() } | ForEach-Object { ConvertFrom-Json $_ })
        $cdx = @($requests | Where-Object { $_.path -like '/cdx/*' })
        Assert-Equal 3 $cdx.Count 'e2e: CDX paged by resumeKey (429 once, then two pages)'
        Assert-True ($cdx[2].query -match 'resumeKey=') 'e2e: second page sent the resumeKey'
        $gaps = @()
        for ($i = 1; $i -lt $requests.Count; $i++) { $gaps += ($requests[$i].t - $requests[$i - 1].t) }
        $minGap = ($gaps | Measure-Object -Minimum).Minimum
        # Arrival gaps at the mock jitter by tens of ms around the 0.4 s start-to-start pacing (unit-tested exactly above).
        Assert-True ($minGap -ge 0.3) "e2e: requests paced about 0.4 s apart (smallest arrival gap $minGap)"
        $afterRetryAfter = $requests[1].t - $requests[0].t
        Assert-True ($afterRetryAfter -ge 1.9) "e2e: Retry-After 2 honoured on the CDX 429 ($afterRetryAfter s)"
        Assert-True (@($requests | Where-Object { $_.ua -match '@|mailto|http' }).Count -eq 0) 'e2e: User-Agent carries no email or URL'
        Assert-True (@($requests | Where-Object { $_.path -notmatch '^/(cdx/search/cdx|web/\d+id_/https?://www\.camelotherald\.com/guilds/)' }).Count -eq 0) 'e2e: nothing fetched outside the job pattern'
        $broken = 'http://www.camelotherald.com/guilds/broken.php 20030707000000'
        Assert-Equal 'error 1' ($m1[$broken].status + ' ' + $m1[$broken].errors) 'e2e: an always-500 page is given up for this run, and the run goes on'
        Assert-True ($null -ne $m1['http://www.camelotherald.com/guilds/c.php 20030707000000']) 'e2e: the page after the broken one was still fetched'
        Assert-True ($run1 -match 'SUMMARY finished: jobs=1 indexed=7 fetched=4 skipped=0 failed=3') "e2e: summary line`n$run1"
        $logs = @(Get-ChildItem -LiteralPath (Join-Path (Join-Path $work 'out') '_logs') -Filter 'run-*.log')
        Assert-True ($logs.Count -ge 1) 'e2e: run log written'

        function Get-NewRequests([int]$skip) { return @([IO.File]::ReadAllLines($reqLog) | Where-Object { $_.Trim() } | Select-Object -Skip $skip | ForEach-Object { ConvertFrom-Json $_ }) }
        function Get-RequestCount { return @([IO.File]::ReadAllLines($reqLog) | Where-Object { $_.Trim() }).Count }

        # Run 2: the index and the saved pages are not requested again; only the broken page is tried again.
        $before = Get-RequestCount
        Start-Sleep -Milliseconds 1100   # a new log file name
        $run2 = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -ArchiveBase $base 2>&1 | Out-String
        Assert-Equal 0 $LASTEXITCODE "e2e run 2 exit code`n$run2"
        $new2 = Get-NewRequests $before
        Assert-Equal 3 $new2.Count 'e2e: resumed run asks only for the broken page (3 tries)'
        Assert-Equal 0 @($new2 | Where-Object { $_.path -notlike '*broken.php' }).Count 'e2e: resumed run requests nothing already saved, and no index'
        Assert-True ($run2 -match 'SUMMARY finished: jobs=1 indexed=7 fetched=0 skipped=6 failed=1') "e2e: second summary`n$run2"

        # Run 3: a deleted page is fetched again; the broken page fails a third run and is then left alone.
        Remove-Item -LiteralPath (Join-Path $jobDir $saved[0].path)
        $after = Get-RequestCount
        $run3 = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -ArchiveBase $base 2>&1 | Out-String
        $new3 = @(Get-NewRequests $after | Where-Object { $_.path -notlike '*broken.php' })
        Assert-True ($new3.Count -ge 1 -and $new3.Count -le 2 -and $new3[0].path -like ('*' + $saved[0].url.Substring(7))) "e2e: only the deleted page is fetched again ($($new3.Count) requests)"
        Assert-True ($run3 -match 'fetched=1 skipped=5') "e2e: third summary`n$run3"
        Assert-Equal 'failed 3' ((Read-Manifest (Join-Path $jobDir 'manifest.jsonl'))[$broken].status + ' ' + (Read-Manifest (Join-Path $jobDir 'manifest.jsonl'))[$broken].errors) 'e2e: broken page marked failed after 3 runs'
        $after3 = Get-RequestCount
        $run3b = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -ArchiveBase $base 2>&1 | Out-String
        Assert-Equal $after3 (Get-RequestCount) 'e2e: a finished job makes no requests at all'
        Assert-True ($run3b -match 'fetched=0 skipped=7') "e2e: finished summary`n$run3b"

        # Run 4: a new job over the daily cap stops cleanly, before any request.
        $capJob = Join-Path $work 'cap.json'
        [IO.File]::WriteAllText($capJob, $e2eJob.Replace('"e2e"', '"e2e-cap"'))
        $capped = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -DailyCap 1 -ArchiveBase $base -Job $capJob 2>&1 | Out-String
        $after4 = Get-RequestCount
        Assert-Equal 0 $LASTEXITCODE "e2e: capped run exits 0`n$capped"
        Assert-True ($capped -match 'Stopped: the daily cap of 1 requests' -and $capped -match 'SUMMARY stopped early') "e2e: daily cap stops the run`n$capped"
        Assert-Equal $after3 $after4 'e2e: no request over the daily cap'

        # Run 5: an index-only job lists captures in index.csv and downloads nothing.
        $mapJob = Join-Path $work 'map.json'
        [IO.File]::WriteAllText($mapJob, $e2eJob.Replace('"e2e"', '"e2e-map", "indexOnly": true'))
        $run5 = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -ArchiveBase $base -Job $mapJob 2>&1 | Out-String
        Assert-Equal 0 $LASTEXITCODE "e2e: index-only run exits 0`n$run5"
        $new5 = Get-NewRequests $after4
        Assert-Equal 0 @($new5 | Where-Object { $_.path -like '/web/*' }).Count 'e2e: index-only fetches no pages'
        $mapCsv = @(Import-Csv -LiteralPath (Join-Path (Join-Path (Join-Path $work 'out') 'e2e-map') 'index.csv'))
        Assert-Equal 7 $mapCsv.Count 'e2e: index-only lists the in-scope captures'
        Assert-True ($mapCsv[0].wayback_url -like 'https://web.archive.org/web/*id_/http://www.camelotherald.com/guilds/*') 'e2e: index-only rows carry the real id_ URL'
        Assert-True ($csv[0].PSObject.Properties.Name -contains 'patch') 'e2e: index.csv has a patch column'

        # Run 6: a second job wanting the same pages copies them from the first job instead of fetching them.
        $twinJob = Join-Path $work 'twin.json'
        [IO.File]::WriteAllText($twinJob, $e2eJob.Replace('"e2e"', '"e2e-twin", "exclude": ["gone|jump|broken"]'))
        $before6 = Get-RequestCount
        $run6 = & $exe -NoProfile -ExecutionPolicy Bypass -File $script -Root $work -DelaySeconds 0.4 -ArchiveBase $base -Job $twinJob 2>&1 | Out-String
        Assert-Equal 0 $LASTEXITCODE "e2e: twin run exits 0`n$run6"
        Assert-Equal 0 @(Get-NewRequests $before6 | Where-Object { $_.path -like '/web/*' }).Count 'e2e: pages another job saved are not fetched again'
        Assert-True ($run6 -match 'fetched=0 skipped=0 failed=0 copied=4') "e2e: twin summary`n$run6"
        $twinDir = Join-Path (Join-Path $work 'out') 'e2e-twin'
        $tm = Read-Manifest (Join-Path $twinDir 'manifest.jsonl')
        foreach ($e in $tm.Values) {
            Assert-Equal 'e2e' $e.copiedFrom ('e2e: copy recorded ' + $e.url)
            Assert-Equal $e.sha256 (Get-Sha256Hex ([IO.File]::ReadAllBytes((Join-Path $twinDir $e.path)))) ('e2e: copy intact ' + $e.url)
        }
    } finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ("{0} passed, {1} failed" -f $script:Passes, $script:Failures)
if ($script:Failures -gt 0) { exit 1 }
exit 0
