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
Assert-True ($q2.EndsWith('&resumeKey=com%2Cx%29%2F%202003')) 'cdx query: resumeKey escaped'
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
    Assert-True ([string](Get-Prop $info.Def 'note' '')).Length -gt 20 ("starter job has a note: " + $jf.Name)
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
        Assert-True ($minGap -ge 0.38) "e2e: requests at least 0.4 s apart (smallest gap $minGap)"
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
    } finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ("{0} passed, {1} failed" -f $script:Passes, $script:Failures)
if ($script:Failures -gt 0) { exit 1 }
exit 0
