# wayback-fetch.ps1 - a slow, polite Wayback Machine fetcher for the 2002-2005 research.
#
# Runs on Windows PowerShell 5.1 with nothing installed. RUN-WAYBACK-FETCH.cmd starts it.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File wayback-fetch.ps1               (every jobs\*.json)
#   powershell -NoProfile -ExecutionPolicy Bypass -File wayback-fetch.ps1 -Job x.json   (one job)
#
# For each job it asks the Wayback CDX index which captures exist (paged with resumeKey, capped), then downloads
# those captures one at a time through the id_ URL form (the raw page, no Wayback toolbar). It never follows links:
# only URLs the index returned for the job's own patterns are fetched. Everything is resumable: stop it any time
# (close the window) and run it again; saved pages are skipped. See README.md.
#
# Keep this file ASCII and free of PowerShell 7-only syntax: it must run on Windows PowerShell 5.1.
param(
    # Job files (.json). Empty: every jobs\*.json under -Root, in name order.
    [string[]]$Job = @(),
    # The folder that holds jobs\, out\ and wayback-fetch.config.json.
    [string]$Root = '',
    # Overrides for wayback-fetch.config.json (negative = use the config file).
    [double]$DelaySeconds = -1,
    [int]$DailyCap = -1,
    # Only build the capture index; download nothing.
    [switch]$IndexOnly,
    # Testing only: talk to a local mock archive instead of the real one.
    [string]$ArchiveBase = 'https://web.archive.org'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------
# Settings
# ---------------------------------------------------------------------------------------------------------------

function Get-DefaultConfig {
    return [ordered]@{
        delaySeconds            = 3.0      # at least this long between the starts of two requests
        dailyRequestCap         = 1500     # every request counts (index pages, pages, retries, redirects)
        maxRetries              = 5        # retries after the first try, for 429 / 5xx / resets / timeouts
        backoffBaseSeconds      = 10.0     # 10, 20, 40, 80, 160 ...
        backoffMaxSeconds       = 600.0
        maxRetryAfterSeconds    = 900.0    # a longer Retry-After stops the run instead of sleeping
        timeoutSeconds          = 120      # big CDX index queries can be slow
        maxBytes                = 10485760 # pages larger than this are not saved
        userAgent               = 'wayback-fetch/1.0 (personal archival research; one request at a time)'
        cdxPageSize             = 500
        cdxMaxPages             = 5
        maxRedirects            = 3
    }
}

function Get-Prop($obj, [string]$name, $default) {
    if ($null -eq $obj) { return $default }
    if ($obj -is [System.Collections.IDictionary]) {
        if ($obj.Contains($name) -and $null -ne $obj[$name]) { return $obj[$name] }
        return $default
    }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p -or $null -eq $p.Value) { return $default }
    return $p.Value
}

function Read-Config([string]$path) {
    $cfg = Get-DefaultConfig
    if ($path -and (Test-Path -LiteralPath $path)) {
        $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($key in @($cfg.Keys)) {
            $v = Get-Prop $raw $key $null
            if ($null -ne $v) { $cfg[$key] = $v }
        }
    }
    return $cfg
}

# ---------------------------------------------------------------------------------------------------------------
# Small pure helpers (unit tested in tests\run-tests.ps1)
# ---------------------------------------------------------------------------------------------------------------

function Get-Sha256Hex([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $h = $sha.ComputeHash($bytes) } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($h) -replace '-', '').ToLowerInvariant()
}

function Get-Utf8Bytes([string]$s) { return [System.Text.Encoding]::UTF8.GetBytes($s) }

# Splits "http://www.Example.com:80/a/b?c=1" into host (lower case, no port) and path+query.
function Split-OriginalUrl([string]$original) {
    $s = $original
    $m = [regex]::Match($s, '^[A-Za-z][A-Za-z0-9+.-]*://')
    if ($m.Success) { $s = $s.Substring($m.Length) }
    $slash = $s.IndexOfAny([char[]]@('/', '?', '#'))
    if ($slash -lt 0) { $hostPart = $s; $rest = '/' } else { $hostPart = $s.Substring(0, $slash); $rest = $s.Substring($slash) }
    if ($rest.StartsWith('?')) { $rest = '/' + $rest }
    $hash = $rest.IndexOf('#')
    if ($hash -ge 0) { $rest = $rest.Substring(0, $hash) }
    $at = $hostPart.LastIndexOf('@')
    if ($at -ge 0) { $hostPart = $hostPart.Substring($at + 1) }
    $hostPart = ($hostPart -replace ':\d+$', '').ToLowerInvariant().TrimEnd('.')
    return [pscustomobject]@{ Host = $hostPart; PathQuery = $rest }
}

# The comparable form of a URL or pattern: lower case, no scheme, no leading www., no port.
function Get-NormalizedUrl([string]$url) {
    $u = Split-OriginalUrl $url
    $h = $u.Host -replace '^www\d*\.', ''
    return ($h + $u.PathQuery).ToLowerInvariant()
}

function Get-NormalizedHost([string]$url) {
    return ((Split-OriginalUrl $url).Host -replace '^www\d*\.', '')
}

# Like Get-NormalizedUrl, with &amp; decoded, the query parameters sorted and a trailing / dropped. The archive's
# index (SURT urlkey) sorts query parameters, so "?s=Merlin&c=1" and "?c=1&s=Merlin" are the same capture to it.
function Get-CanonicalUrl([string]$url) {
    $n = (Get-NormalizedUrl ($url -replace '&amp;', '&'))
    $q = $n.IndexOf('?')
    if ($q -lt 0) { return $n.TrimEnd('/') }
    $path = $n.Substring(0, $q).TrimEnd('/')
    $params = @($n.Substring($q + 1).Split('&') | Where-Object { $_ -ne '' } | Sort-Object { $_ } -CaseSensitive)
    if ($params.Count -eq 0) { return $path }
    return $path + '?' + ($params -join '&')
}

# The key that makes two captures "the same page" for a job's dedupeKey (a .NET regex on the normalized URL; its
# first group, or the whole match, plus the host). $null when there is no dedupeKey or it does not match.
function Get-DedupeKey($row, $jd, $target) {
    $rx = [string](Get-Prop $target 'dedupeKey' (Get-Prop $jd 'dedupeKey' ''))
    if (-not $rx) { return $null }
    $orig = [string](Get-Prop $row 'original' '')
    $m = [regex]::Match((Get-NormalizedUrl ($orig -replace '&amp;', '&')), $rx)
    if (-not $m.Success) { return $null }
    $v = $m.Value
    if ($m.Groups.Count -gt 1 -and $m.Groups[1].Success) { $v = $m.Groups[1].Value }
    return (Get-NormalizedHost $orig) + '|' + $v
}

# Where a capture is saved, relative to the job's folder: <host>\<path-hash>_<timestamp>.<ext>
function Get-SaveRelativePath([string]$original, [string]$timestamp, [string]$mimetype) {
    $u = Split-OriginalUrl $original
    $hostDir = ($u.Host -replace '[^a-z0-9.-]', '_')
    if (-not $hostDir) { $hostDir = '_' }
    $hash = (Get-Sha256Hex (Get-Utf8Bytes $u.PathQuery)).Substring(0, 16)
    $ext = 'html'
    if ($mimetype) {
        $mt = $mimetype.ToLowerInvariant()
        if ($mt -eq 'text/plain') { $ext = 'txt' }
        elseif ($mt -eq 'text/css') { $ext = 'css' }
        elseif ($mt -match 'xml' -and $mt -notmatch 'xhtml') { $ext = 'xml' }
        elseif ($mt -match '^image/(gif|png|jpeg)$') { $ext = $Matches[1] -replace 'jpeg', 'jpg' }
        elseif ($mt -notmatch 'html' -and $mt -ne 'unk') { $ext = 'bin' }
    }
    return $hostDir + [IO.Path]::DirectorySeparatorChar + $hash + '_' + $timestamp + '.' + $ext
}

# The capture URL without the Wayback toolbar or link rewriting.
function Get-IdUrl([string]$archiveBase, [string]$timestamp, [string]$original) {
    return $archiveBase.TrimEnd('/') + '/web/' + $timestamp + 'id_/' + $original
}

# Builds one CDX index query. $target is a job target (url, matchType, collapse, cdxFilters), $jd holds from/to/mimetypes.
function Get-CdxQueryUrl([string]$archiveBase, $jd, $target, [int]$pageSize, [string]$resumeKey) {
    $q = New-Object System.Collections.Generic.List[string]
    $q.Add('url=' + [Uri]::EscapeDataString([string](Get-Prop $target 'url' '')))
    $mt = [string](Get-Prop $target 'matchType' 'prefix')
    if ($mt -ne 'exact') { $q.Add('matchType=' + $mt) }
    $from = [string](Get-Prop $target 'from' (Get-Prop $jd 'from' ''))
    $to = [string](Get-Prop $target 'to' (Get-Prop $jd 'to' ''))
    if ($from) { $q.Add('from=' + $from) }
    if ($to) { $q.Add('to=' + $to) }
    $q.Add('output=json')
    $q.Add('fl=urlkey,timestamp,original,mimetype,statuscode,digest,length')
    $q.Add('filter=' + [Uri]::EscapeDataString('statuscode:200'))
    $mimes = @(Get-Prop $target 'mimetypes' (Get-Prop $jd 'mimetypes' @('text/html')))
    if ($mimes.Count -gt 0) {
        $alt = ($mimes | ForEach-Object { [regex]::Escape([string]$_) }) -join '|'
        $q.Add('filter=' + [Uri]::EscapeDataString('mimetype:(' + $alt + ')'))
    }
    foreach ($f in @(Get-Prop $target 'cdxFilters' @())) { $q.Add('filter=' + [Uri]::EscapeDataString([string]$f)) }
    $collapse = [string](Get-Prop $target 'collapse' 'urlkey')
    if ($collapse -and $collapse -ne 'none') { $q.Add('collapse=' + [Uri]::EscapeDataString($collapse)) }
    $q.Add('limit=' + $pageSize)
    $q.Add('showResumeKey=true')
    if ($resumeKey) { $q.Add('resumeKey=' + [Uri]::EscapeDataString($resumeKey)) }
    return $archiveBase.TrimEnd('/') + '/cdx/search/cdx?' + ($q -join '&')
}

# Parses a CDX output=json page: a header row, data rows, and (with showResumeKey) an empty row then [resumeKey].
# Returns Rows (objects with the header's field names) and ResumeKey ($null on the last page).
function ConvertFrom-CdxJson([string]$text) {
    $rows = New-Object System.Collections.Generic.List[object]
    $result = [pscustomobject]@{ Rows = $rows; ResumeKey = $null }
    if ($null -eq $text -or $text.Trim().Length -eq 0) { return $result }
    # Wrapping keeps PowerShell 5.1 and 7 from unrolling the top-level array differently.
    $wrapped = ConvertFrom-Json ('{"r":' + $text.Trim() + '}')
    $all = @($wrapped.r)
    $end = $all.Count
    if ($end -ge 2 -and @($all[$end - 2]).Count -eq 0 -and @($all[$end - 1]).Count -eq 1) {
        $result.ResumeKey = [string](@($all[$end - 1])[0])
        $end = $end - 2
    }
    if ($end -eq 0) { return $result }
    $header = @($all[0] | ForEach-Object { [string]$_ })
    if ($header.Count -eq 0 -or $header[0] -ne 'urlkey') {
        $header = @('urlkey', 'timestamp', 'original', 'mimetype', 'statuscode', 'digest', 'length')
        $start = 0
    } else { $start = 1 }
    for ($i = $start; $i -lt $end; $i++) {
        $cells = @($all[$i])
        if ($cells.Count -eq 0) { continue }
        $o = [ordered]@{}
        for ($c = 0; $c -lt $header.Count; $c++) {
            if ($c -lt $cells.Count) { $o[$header[$c]] = [string]$cells[$c] } else { $o[$header[$c]] = '' }
        }
        $rows.Add([pscustomobject]$o)
    }
    return $result
}

# One CDX-style filter ("[!]field:regex", Java full-match semantics) applied locally to a row.
function Test-CdxFilter($row, [string]$filter) {
    $neg = $false
    $f = $filter
    if ($f.StartsWith('!')) { $neg = $true; $f = $f.Substring(1) }
    $colon = $f.IndexOf(':')
    if ($colon -lt 1) { return $true }
    $field = $f.Substring(0, $colon)
    $pattern = $f.Substring($colon + 1)
    if ($field.StartsWith('~')) { $field = $field.Substring(1); $pattern = '.*' + $pattern + '.*' }
    $value = [string](Get-Prop $row $field '')
    $hit = [regex]::IsMatch($value, '^(?:' + $pattern + ')$')
    if ($neg) { return -not $hit }
    return $hit
}

# Is this index row inside the job target's own pattern? Nothing else is ever fetched.
function Test-InScope($row, $jd, $target) {
    $original = [string](Get-Prop $row 'original' '')
    $ts = [string](Get-Prop $row 'timestamp' '')
    if (-not $original -or $ts -notmatch '^\d{4,14}$') { return $false }
    if ($original -notmatch '^(?i)https?://') { return $false }
    $mt = [string](Get-Prop $target 'matchType' 'prefix')
    $pattern = [string](Get-Prop $target 'url' '')
    $n = Get-NormalizedUrl $original
    switch ($mt) {
        'exact' { if ((Get-CanonicalUrl $original) -ne (Get-CanonicalUrl $pattern)) { return $false } }
        'prefix' {
            $p = Get-NormalizedUrl $pattern
            # A host-only pattern normalizes to "host/", so it cannot leak to longer host names.
            if (-not $n.StartsWith($p)) { return $false }
        }
        'host' { if ((Get-NormalizedHost $original) -ne (Get-NormalizedHost $pattern)) { return $false } }
        'domain' {
            $h = Get-NormalizedHost $original
            $d = Get-NormalizedHost $pattern
            if ($h -ne $d -and -not $h.EndsWith('.' + $d)) { return $false }
        }
        default { return $false }
    }
    $from = [string](Get-Prop $target 'from' (Get-Prop $jd 'from' ''))
    $to = [string](Get-Prop $target 'to' (Get-Prop $jd 'to' ''))
    if ($from -and $ts.Substring(0, [Math]::Min($ts.Length, $from.Length)) -lt $from) { return $false }
    if ($to -and $ts.Substring(0, [Math]::Min($ts.Length, $to.Length)) -gt $to) { return $false }
    if ([string](Get-Prop $row 'statuscode' '200') -ne '200') { return $false }
    foreach ($f in @(Get-Prop $target 'cdxFilters' @())) { if (-not (Test-CdxFilter $row ([string]$f))) { return $false } }
    $includes = @(Get-Prop $target 'include' @()) + @(Get-Prop $jd 'include' @())
    foreach ($inc in $includes) { if ($original -notmatch [string]$inc) { return $false } }
    $excludes = @(Get-Prop $target 'exclude' @()) + @(Get-Prop $jd 'exclude' @())
    foreach ($exc in $excludes) { if ($original -match [string]$exc) { return $false } }
    return $true
}

# Seconds to wait before the next request so request starts are at least $interval apart.
function Get-PaceDelay([Nullable[DateTime]]$lastStart, [DateTime]$now, [double]$interval) {
    if ($null -eq $lastStart) { return 0.0 }
    $elapsed = ($now - [DateTime]$lastStart).TotalSeconds
    if ($elapsed -ge $interval) { return 0.0 }
    return [Math]::Round($interval - $elapsed, 3)
}

# Retry-After as seconds: either a number of seconds or an HTTP date. $null when absent or unreadable.
function Get-RetryAfterSeconds([string]$header, [DateTime]$nowUtc) {
    if (-not $header) { return $null }
    $h = $header.Trim()
    $n = 0
    if ([int]::TryParse($h, [ref]$n)) { return [double][Math]::Max(0, $n) }
    $d = [DateTime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    if ([DateTime]::TryParse($h, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$d)) {
        return [double][Math]::Max(0, [Math]::Ceiling(($d - $nowUtc).TotalSeconds))
    }
    return $null
}

# What to do with one response. Kind: ok | redirect | retry | stop | permanent.
# $attempt counts tries already made for this URL (1 after the first). Wait is in seconds.
function Get-RetryDecision([int]$status, [string]$errorKind, [int]$attempt, $retryAfter, $cfg) {
    if ($status -ge 200 -and $status -lt 300) { return [pscustomobject]@{ Kind = 'ok'; Wait = 0.0; Reason = "$status" } }
    if ($status -ge 300 -and $status -lt 400) { return [pscustomobject]@{ Kind = 'redirect'; Wait = 0.0; Reason = "$status" } }
    $transient = $false
    $reason = ''
    if ($status -eq 0) { $transient = $true; $reason = $errorKind }
    elseif ($status -eq 429 -or $status -eq 408 -or $status -ge 500) { $transient = $true; $reason = "HTTP $status" }
    if (-not $transient) { return [pscustomobject]@{ Kind = 'permanent'; Wait = 0.0; Reason = "HTTP $status" } }
    if ($null -ne $retryAfter -and [double]$retryAfter -gt [double]$cfg.maxRetryAfterSeconds) {
        return [pscustomobject]@{ Kind = 'stop'; Wait = [double]$retryAfter; Throttled = $true; Reason = "$reason, Retry-After $retryAfter s is longer than the $($cfg.maxRetryAfterSeconds) s limit" }
    }
    if ($attempt -gt [int]$cfg.maxRetries) {
        # Throttled: the archive itself asks us to slow down, so the whole run stops. Otherwise (5xx, resets,
        # timeouts) the caller may give up on this one URL and go on, see Invoke-PoliteGet.
        return [pscustomobject]@{ Kind = 'stop'; Wait = 0.0; Throttled = ($status -eq 429); Reason = "$reason after $attempt tries" }
    }
    $backoff = [Math]::Min([double]$cfg.backoffMaxSeconds, [double]$cfg.backoffBaseSeconds * [Math]::Pow(2, $attempt - 1))
    $wait = $backoff
    if ($null -ne $retryAfter -and [double]$retryAfter -gt $wait) { $wait = [double]$retryAfter }
    return [pscustomobject]@{ Kind = 'retry'; Wait = $wait; Reason = $reason }
}

# pick (job or target): which captures of one page to keep when the index lists several (collapse digest or none).
#   first            every row as listed (the default; with collapse=urlkey that is the earliest capture)
#   latest           only the latest capture of each page
#   latest-per-year  the latest capture of each page in each year (one per patch era)
# A page is its dedupeKey when the job has one, else its canonical URL. Order of first appearance is kept.
function Select-Captures($rows, $jd, $target) {
    $pick = [string](Get-Prop $target 'pick' (Get-Prop $jd 'pick' 'first'))
    if ($pick -eq 'first') { return ,$rows }
    $best = [ordered]@{}
    foreach ($r in $rows) {
        $k = Get-DedupeKey $r $jd $target
        if ($null -eq $k) { $k = Get-CanonicalUrl ([string]$r.original) }
        if ($pick -eq 'latest-per-year') { $k = $k + '|' + ([string]$r.timestamp).Substring(0, 4) }
        if (-not $best.Contains($k) -or [string]$r.timestamp -gt [string]$best[$k].timestamp) { $best[$k] = $r }
    }
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($v in $best.Values) { $out.Add($v) }
    return ,$out
}

# Patch versions a page names ("Version 1.65", "patch 1.66b", "v1.62", "1.70 release notes"), in order of first
# mention, at most $max. The 2002-2005 era runs from about 1.4x (classic) to 1.7x (Trials of Atlantis).
function Get-PatchMentions([string]$text, [int]$max = 3) {
    $found = New-Object System.Collections.Generic.List[object]
    $rxs = @('(?i)\b(?:version|patch|v\.?)\s*(1\.[3-8]\d[a-z]?)(?![\d.])', '(?i)(?<![\d.])(1\.[3-8]\d[a-z]?)\s+(?:patch|release)')
    foreach ($rx in $rxs) { foreach ($m in [regex]::Matches($text, $rx)) { $found.Add([pscustomobject]@{ At = $m.Index; V = $m.Groups[1].Value.ToLowerInvariant() }) } }
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($f in ($found | Sort-Object At)) {
        if (-not $out.Contains($f.V)) { $out.Add($f.V) }
        if ($out.Count -ge $max) { break }
    }
    return ($out -join ';')
}

# Reads a manifest (JSON lines). The last line for a url+timestamp wins; a torn last line is ignored.
function Read-Manifest([string]$path) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if (-not $line.Trim()) { continue }
        try { $e = ConvertFrom-Json $line } catch { continue }
        $key = [string](Get-Prop $e 'url' '') + ' ' + [string](Get-Prop $e 'timestamp' '')
        $map[$key] = $e
    }
    return $map
}

# Done means: saved and the file is still there, or a permanent answer (missing, blocked, too large, out of scope).
function Test-ManifestDone($entry, [string]$jobDir) {
    if ($null -eq $entry) { return $false }
    $status = [string](Get-Prop $entry 'status' '')
    if ($status -eq 'saved') {
        $rel = [string](Get-Prop $entry 'path' '')
        if (-not $rel) { return $false }
        $full = Join-Path $jobDir $rel
        if (-not (Test-Path -LiteralPath $full)) { return $false }
        return ((Get-Item -LiteralPath $full).Length -eq [long](Get-Prop $entry 'bytes' -1))
    }
    return @('missing', 'blocked', 'too-large', 'out-of-scope', 'failed') -contains $status
}

# Captures other jobs in out\ already saved (url + timestamp -> that job's folder and manifest entry), so a page two
# jobs both want is downloaded once and copied.
function Get-SavedElsewhere([string]$outRoot, [string]$jobName) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $outRoot)) { return $map }
    foreach ($d in @(Get-ChildItem -LiteralPath $outRoot -Directory)) {
        if ($d.Name -eq $jobName -or $d.Name.StartsWith('_')) { continue }
        $mf = Join-Path $d.FullName 'manifest.jsonl'
        if (-not (Test-Path -LiteralPath $mf)) { continue }
        $m = Read-Manifest $mf
        foreach ($k in $m.Keys) {
            if ([string](Get-Prop $m[$k] 'status' '') -eq 'saved' -and -not $map.ContainsKey($k)) {
                $map[$k] = [pscustomobject]@{ Dir = $d.FullName; Job = $d.Name; Entry = $m[$k] }
            }
        }
    }
    return $map
}

# ---------------------------------------------------------------------------------------------------------------
# I/O: logging, files, the daily counter, HTTP (replaceable in tests)
# ---------------------------------------------------------------------------------------------------------------

$script:Utf8 = New-Object System.Text.UTF8Encoding($false)
$script:LogPath = $null
$script:LastRequestStart = $null
$script:RequestsThisRun = 0
$script:Config = Get-DefaultConfig
$script:ArchiveBase = $ArchiveBase
$script:StateDir = $null
$script:Clock = { [DateTime]::UtcNow }
$script:Sleeper = { param([double]$seconds) if ($seconds -gt 0) { Start-Sleep -Milliseconds ([int][Math]::Ceiling($seconds * 1000)) } }

function Write-RunLog([string]$message) {
    $line = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '  ' + $message
    Write-Host $line
    if ($script:LogPath) { [IO.File]::AppendAllText($script:LogPath, $line + "`r`n", $script:Utf8) }
}

function Add-TextLine([string]$path, [string]$line) {
    [IO.File]::AppendAllText($path, $line + "`n", $script:Utf8)
}

function Save-BytesAtomically([string]$path, [byte[]]$bytes) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $part = $path + '.part'
    [IO.File]::WriteAllBytes($part, $bytes)
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    [IO.File]::Move($part, $path)
}

function Get-DailyCounterPath {
    return Join-Path $script:StateDir ('requests-' + (Get-Date).ToString('yyyy-MM-dd') + '.txt')
}

function Get-DailyCount {
    $p = Get-DailyCounterPath
    if (-not (Test-Path -LiteralPath $p)) { return 0 }
    $n = 0
    [void][int]::TryParse(([IO.File]::ReadAllText($p)).Trim(), [ref]$n)
    return $n
}

function Add-DailyCount {
    $n = (Get-DailyCount) + 1
    [IO.File]::WriteAllText((Get-DailyCounterPath), [string]$n, $script:Utf8)
    return $n
}

# The default transport: one GET with HttpWebRequest (same behaviour on PowerShell 5.1 and 7), no redirects
# followed. Returns Status (0 = no HTTP answer), Bytes, Headers (lower-case names), ErrorKind.
$script:Transport = {
    param([string]$url, $cfg)
    $result = [pscustomobject]@{ Status = 0; Bytes = [byte[]]@(); Headers = @{}; ErrorKind = ''; TooLarge = $false }
    $req = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($url)
    $req.Method = 'GET'
    $req.UserAgent = [string]$cfg.userAgent
    $req.AllowAutoRedirect = $false
    $req.Timeout = [int]$cfg.timeoutSeconds * 1000
    $req.ReadWriteTimeout = [int]$cfg.timeoutSeconds * 1000
    $req.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    $req.Accept = 'text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.5'
    $resp = $null
    try {
        try {
            $resp = [System.Net.HttpWebResponse]$req.GetResponse()
        } catch {
            $ex = $_.Exception
            while ($null -ne $ex -and -not ($ex -is [System.Net.WebException])) { $ex = $ex.InnerException }
            if ($null -ne $ex -and $null -ne $ex.Response) {
                $resp = [System.Net.HttpWebResponse]$ex.Response
            } else {
                $kind = 'connection error'
                if ($null -ne $ex) { $kind = [string]$ex.Status }
                $result.ErrorKind = $kind + ': ' + $_.Exception.Message
                return $result
            }
        }
        $result.Status = [int]$resp.StatusCode
        foreach ($name in $resp.Headers.AllKeys) { $result.Headers[$name.ToLowerInvariant()] = $resp.Headers[$name] }
        $stream = $resp.GetResponseStream()
        $ms = New-Object System.IO.MemoryStream
        $buffer = New-Object byte[] 65536
        try {
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $ms.Write($buffer, 0, $read)
                if ($ms.Length -gt [long]$cfg.maxBytes) { $result.TooLarge = $true; break }
            }
        } catch {
            $result.Status = 0
            $result.ErrorKind = 'read failed: ' + $_.Exception.Message
            return $result
        } finally { $stream.Dispose() }
        $result.Bytes = $ms.ToArray()
    } finally {
        if ($null -ne $resp) { $resp.Close() }
    }
    return $result
}

# Thrown to end the run politely (daily cap reached, the archive asks us to stop, too many failures).
function New-StopRun([string]$why) { return New-Object System.Exception ('STOP-RUN: ' + $why) }
# Thrown when one URL keeps failing with 5xx, resets or timeouts: the caller skips that URL for this run.
function New-GiveUp([string]$why) { return New-Object System.Exception ('GIVE-UP: ' + $why) }
$script:GiveUpsInARow = 0
$script:MaxGiveUpsInARow = 3

# One polite GET: waits for the pace, counts against the daily cap, retries with back-off and Retry-After.
# Returns the transport result of the final try (2xx, 3xx or a permanent 4xx). Throws STOP-RUN when the run must
# stop: the daily cap, a 429 that outlasts the retries, a too-long Retry-After, or 3 URLs in a row given up on.
# Throws GIVE-UP when only this URL keeps failing.
function Invoke-PoliteGet([string]$url) {
    $cfg = $script:Config
    $attempt = 0
    while ($true) {
        if ((Get-DailyCount) -ge [int]$cfg.dailyRequestCap) {
            throw (New-StopRun "the daily cap of $($cfg.dailyRequestCap) requests is reached; run again tomorrow")
        }
        $wait = Get-PaceDelay $script:LastRequestStart (& $script:Clock) ([double]$cfg.delaySeconds)
        if ($wait -gt 0) { & $script:Sleeper $wait }
        $script:LastRequestStart = (& $script:Clock)
        [void](Add-DailyCount)
        $script:RequestsThisRun++
        $attempt++
        $r = & $script:Transport $url $cfg
        $retryAfter = Get-RetryAfterSeconds ([string]$r.Headers['retry-after']) ([DateTime]::UtcNow)
        $d = Get-RetryDecision ([int]$r.Status) ([string]$r.ErrorKind) $attempt $retryAfter $cfg
        switch ($d.Kind) {
            'retry' {
                Write-RunLog ("  {0}; waiting {1:0} s before try {2}" -f $d.Reason, $d.Wait, ($attempt + 1))
                & $script:Sleeper $d.Wait
            }
            'stop' {
                if ($d.Throttled) { throw (New-StopRun ("the archive asks us to slow down ({0}); stopping, run again later" -f $d.Reason)) }
                $script:GiveUpsInARow++
                if ($script:GiveUpsInARow -ge $script:MaxGiveUpsInARow) {
                    throw (New-StopRun ("the archive is not answering normally ({0}, {1} addresses in a row); stopping, run again later" -f $d.Reason, $script:GiveUpsInARow))
                }
                throw (New-GiveUp $d.Reason)
            }
            default { $script:GiveUpsInARow = 0; return $r }
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Jobs
# ---------------------------------------------------------------------------------------------------------------

function Read-Job([string]$path) {
    $jd = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $name = [string](Get-Prop $jd 'name' ([IO.Path]::GetFileNameWithoutExtension($path)))
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') { throw "job name '$name' may only use letters, digits, '.', '_' and '-'" }
    $targets = @(Get-Prop $jd 'targets' @())
    if ($targets.Count -eq 0) { throw "job '$name' has no targets" }
    foreach ($t in $targets) {
        $u = [string](Get-Prop $t 'url' '')
        $mt = [string](Get-Prop $t 'matchType' 'prefix')
        if (-not $u) { throw "job '$name': a target has no url" }
        if (@('exact', 'prefix', 'host', 'domain') -notcontains $mt) { throw "job '$name': matchType '$mt' is not exact, prefix, host or domain" }
        $pk = [string](Get-Prop $t 'pick' (Get-Prop $jd 'pick' 'first'))
        if (@('first', 'latest', 'latest-per-year') -notcontains $pk) { throw "job '$name': pick '$pk' is not first, latest or latest-per-year" }
        if ($u -match '[*]') { throw "job '$name': use matchType instead of * in '$u'" }
    }
    return [pscustomobject]@{ Name = $name; Def = $jd; Targets = $targets; Path = $path }
}

function Get-TargetKey($jd, $target) {
    # The index state belongs to the exact query, so editing a target starts its index again.
    $q = Get-CdxQueryUrl 'x' $jd $target 0 ''
    return (Get-Sha256Hex (Get-Utf8Bytes $q)).Substring(0, 12)
}

# Pages through the CDX index for one target, resumable. Returns the rows in scope (capped).
function Update-TargetIndex($jobInfo, $target, [int]$index, [string]$jobDir, $stats) {
    $cfg = $script:Config
    $jd = $jobInfo.Def
    $key = Get-TargetKey $jd $target
    $idxDir = Join-Path $jobDir 'index'
    if (-not (Test-Path -LiteralPath $idxDir)) { New-Item -ItemType Directory -Path $idxDir -Force | Out-Null }
    $rowsPath = Join-Path $idxDir ('target' + $index + '-' + $key + '.jsonl')
    $statePath = Join-Path $idxDir ('target' + $index + '-' + $key + '.state.json')
    $state = [pscustomobject]@{ done = $false; pages = 0; resumeKey = ''; rows = 0 }
    if (Test-Path -LiteralPath $statePath) {
        $s = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $state = [pscustomobject]@{ done = [bool](Get-Prop $s 'done' $false); pages = [int](Get-Prop $s 'pages' 0); resumeKey = [string](Get-Prop $s 'resumeKey' ''); rows = [int](Get-Prop $s 'rows' 0) }
    }
    $pageSize = [int](Get-Prop $target 'cdxPageSize' (Get-Prop $jd 'cdxPageSize' $cfg.cdxPageSize))
    $maxPages = [int](Get-Prop $target 'cdxMaxPages' (Get-Prop $jd 'cdxMaxPages' $cfg.cdxMaxPages))
    $label = [string](Get-Prop $target 'url' '') + ' (' + [string](Get-Prop $target 'matchType' 'prefix') + ')'
    if ($state.pages -gt 0 -and -not $state.done -and -not $state.resumeKey) {
        # A page was written but the state was not (stopped in between): start this target's index again.
        $state.pages = 0; $state.rows = 0
        if (Test-Path -LiteralPath $rowsPath) { Remove-Item -LiteralPath $rowsPath -Force }
    }
    while (-not $state.done -and $state.pages -lt $maxPages) {
        $url = Get-CdxQueryUrl $script:ArchiveBase $jd $target $pageSize $state.resumeKey
        Write-RunLog ("  index {0}: page {1}" -f $label, ($state.pages + 1))
        try { $r = Invoke-PoliteGet $url }
        catch {
            if ($_.Exception.Message -notlike 'GIVE-UP: *') { throw }
            Write-RunLog ("  index {0}: {1}; this target is skipped for now and tried again next run" -f $label, $_.Exception.Message.Substring(9))
            $stats.failed++
            break
        }
        $stats.indexRequests++
        if ($r.Status -ne 200) {
            Write-RunLog ("  index {0}: HTTP {1}; this target is skipped for now" -f $label, $r.Status)
            $stats.failed++
            break
        }
        $page = ConvertFrom-CdxJson ($script:Utf8.GetString($r.Bytes))
        $sb = New-Object System.Text.StringBuilder
        foreach ($row in $page.Rows) { [void]$sb.Append(($row | ConvertTo-Json -Compress)).Append("`n") }
        if ($sb.Length -gt 0) { [IO.File]::AppendAllText($rowsPath, $sb.ToString(), $script:Utf8) }
        $state.pages++
        $state.rows += $page.Rows.Count
        if ($page.ResumeKey) { $state.resumeKey = $page.ResumeKey } else { $state.resumeKey = ''; $state.done = $true }
        [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Compress), $script:Utf8)
    }
    if (-not $state.done -and $state.pages -ge $maxPages) {
        Write-RunLog ("  index {0}: stopped at the page cap ({1} pages, {2} rows); raise cdxMaxPages to see more" -f $label, $maxPages, $state.rows)
    }
    $inScope = New-Object System.Collections.Generic.List[object]
    $outOfScope = 0
    if (Test-Path -LiteralPath $rowsPath) {
        foreach ($line in [IO.File]::ReadAllLines($rowsPath)) {
            if (-not $line.Trim()) { continue }
            try { $row = ConvertFrom-Json $line } catch { continue }
            if (Test-InScope $row $jd $target) { $inScope.Add($row) } else { $outOfScope++ }
        }
    }
    if ($outOfScope -gt 0) { Write-RunLog ("  index {0}: {1} rows outside the job's pattern were dropped" -f $label, $outOfScope) }
    return ,$inScope
}

# index.csv: the saved pages (from the manifest), or for an index-only job the captures the index listed.
function Write-JobIndexCsv([string]$jobDir, $manifest, $indexRows, [bool]$indexMode) {
    $csvPath = Join-Path $jobDir 'index.csv'
    $out = New-Object System.Collections.Generic.List[object]
    if ($indexMode) {
        foreach ($r in $indexRows) {
            $out.Add([pscustomobject][ordered]@{
                target = $r.target; timestamp = $r.row.timestamp; original_url = $r.row.original
                mimetype = $r.row.mimetype; length = $r.row.length; wayback_url = (Get-IdUrl 'https://web.archive.org' $r.row.timestamp $r.row.original)
            })
        }
    } else {
        $latin1 = [System.Text.Encoding]::GetEncoding(28591)
        foreach ($e in ($manifest.Values | Sort-Object { [string]$_.path })) {
            if ([string](Get-Prop $e 'status' '') -ne 'saved') { continue }
            $patch = ''
            $file = Join-Path $jobDir ([string]$e.path)
            if (Test-Path -LiteralPath $file) { $patch = Get-PatchMentions ($latin1.GetString([IO.File]::ReadAllBytes($file))) }
            $out.Add([pscustomobject][ordered]@{
                saved_path = $e.path; timestamp = $e.timestamp; patch = $patch; original_url = $e.url; bytes = $e.bytes
                sha256 = $e.sha256; mimetype = $e.mimetype; wayback_url = (Get-IdUrl 'https://web.archive.org' $e.timestamp $e.url)
            })
        }
    }
    if ($out.Count -gt 0) { $out | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8 }
    elseif (Test-Path -LiteralPath $csvPath) { Remove-Item -LiteralPath $csvPath -Force }
    return $out.Count
}

# Fetches one capture, following at most a few Wayback-internal redirects (the same page, another capture time).
function Get-Capture($row, $jd, $target) {
    $cfg = $script:Config
    $ts = [string]$row.timestamp
    $original = [string]$row.original
    for ($hop = 0; $hop -le [int]$cfg.maxRedirects; $hop++) {
        $r = Invoke-PoliteGet (Get-IdUrl $script:ArchiveBase $ts $original)
        if ($r.Status -lt 300 -or $r.Status -ge 400) { return [pscustomobject]@{ Response = $r; Timestamp = $ts } }
        $loc = [string]$r.Headers['location']
        if (-not $loc) { return [pscustomobject]@{ Response = $r; Timestamp = $ts } }
        $abs = (New-Object Uri ((New-Object Uri ($script:ArchiveBase.TrimEnd('/') + '/')), $loc)).AbsoluteUri
        $m = [regex]::Match($abs, '^' + [regex]::Escape($script:ArchiveBase.TrimEnd('/')) + '/web/(\d{4,14})(?:id_)?/(.+)$')
        if (-not $m.Success) { return [pscustomobject]@{ Response = $r; Timestamp = $ts; OutOfScope = $true } }
        $newOriginal = $m.Groups[2].Value
        # Only the same page at another capture time, still inside the job's years and pattern.
        $probe = [pscustomobject]@{ original = $newOriginal; timestamp = $m.Groups[1].Value; statuscode = '200'; urlkey = $row.urlkey; mimetype = $row.mimetype }
        $same = (Get-NormalizedUrl $newOriginal).TrimEnd('/') -eq (Get-NormalizedUrl $original).TrimEnd('/')
        if (-not $same -or -not (Test-InScope $probe $jd $target)) {
            return [pscustomobject]@{ Response = $r; Timestamp = $m.Groups[1].Value; OutOfScope = $true }
        }
        $ts = $m.Groups[1].Value
        $original = $newOriginal
    }
    return [pscustomobject]@{ Response = $r; Timestamp = $ts }
}

function Invoke-Job($jobInfo, [string]$outRoot, $stats) {
    $cfg = $script:Config
    $jd = $jobInfo.Def
    $jobDir = Join-Path $outRoot $jobInfo.Name
    if (-not (Test-Path -LiteralPath $jobDir)) { New-Item -ItemType Directory -Path $jobDir -Force | Out-Null }
    $note = [string](Get-Prop $jd 'note' '')
    Write-RunLog ("job {0}: {1}" -f $jobInfo.Name, $note)
    $indexOnly = $IndexOnly -or [bool](Get-Prop $jd 'indexOnly' $false)

    # 1. Index
    $candidates = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    $i = 0
    foreach ($t in $jobInfo.Targets) {
        $i++
        $rows = Select-Captures (Update-TargetIndex $jobInfo $t $i $jobDir $stats) $jd $t
        $cap = [int](Get-Prop $t 'maxCaptures' 100000)
        $taken = 0
        foreach ($row in $rows) {
            $k = [string]$row.original + ' ' + [string]$row.timestamp
            if ($seen.ContainsKey($k)) { continue }
            # dedupeKey: one capture per thread or character, even when its URLs differ (session ids, extra params).
            $dk = Get-DedupeKey $row $jd $t
            if ($null -ne $dk -and $seen.ContainsKey('dedupe ' + $dk)) { continue }
            if ($taken -ge $cap) { break }
            $seen[$k] = $true
            if ($null -ne $dk) { $seen['dedupe ' + $dk] = $true }
            $taken++
            $candidates.Add([pscustomobject]@{ row = $row; target = $t; targetNo = $i })
        }
        Write-RunLog ("  target {0}: {1} captures in scope, {2} taken (cap {3})" -f $i, $rows.Count, $taken, $cap)
    }
    $jobCap = [int](Get-Prop $jd 'maxCaptures' 100000)
    if ($candidates.Count -gt $jobCap) {
        Write-RunLog ("  job cap: {0} of {1} captures" -f $jobCap, $candidates.Count)
        $trimmed = New-Object System.Collections.Generic.List[object]
        for ($c = 0; $c -lt $jobCap; $c++) { $trimmed.Add($candidates[$c]) }
        $candidates = $trimmed
    }
    $stats.indexed += $candidates.Count

    if ($indexOnly) {
        $listed = Write-JobIndexCsv $jobDir $null (@($candidates | ForEach-Object { [pscustomobject]@{ target = $_.targetNo; row = $_.row } })) $true
        Write-RunLog ("job {0}: index only, {1} captures listed in index.csv" -f $jobInfo.Name, $listed)
        return
    }

    # 2. Fetch
    $manifestPath = Join-Path $jobDir 'manifest.jsonl'
    $manifest = Read-Manifest $manifestPath
    $elsewhere = Get-SavedElsewhere $outRoot $jobInfo.Name
    $todo = @($candidates | Where-Object { -not (Test-ManifestDone $manifest[([string]$_.row.original + ' ' + [string]$_.row.timestamp)] $jobDir) })
    $already = $candidates.Count - $todo.Count
    $stats.skipped += $already
    Write-RunLog ("job {0}: {1} to fetch, {2} already done; about {3:0} minutes at {4} s per request" -f $jobInfo.Name, $todo.Count, $already, ($todo.Count * [double]$cfg.delaySeconds / 60), $cfg.delaySeconds)
    $n = 0
    try {
        foreach ($c in $todo) {
            $n++
            $row = $c.row
            $key = [string]$row.original + ' ' + [string]$row.timestamp
            $prevErrors = [int](Get-Prop $manifest[$key] 'errors' 0)
            $other = $elsewhere[$key]
            if ($null -ne $other -and (Test-ManifestDone $other.Entry $other.Dir)) {
                $bytes = [IO.File]::ReadAllBytes((Join-Path $other.Dir ([string]$other.Entry.path)))
                $sha = Get-Sha256Hex $bytes
                if ($sha -eq [string](Get-Prop $other.Entry 'sha256' '')) {
                    $rel = Get-SaveRelativePath $row.original $row.timestamp $row.mimetype
                    Save-BytesAtomically (Join-Path $jobDir $rel) $bytes
                    $entry = [ordered]@{ url = [string]$row.original; timestamp = [string]$row.timestamp; status = 'saved'; http = 200; bytes = $bytes.Length; sha256 = $sha; path = $rel; mimetype = [string]$row.mimetype; capture = [string](Get-Prop $other.Entry 'capture' $row.timestamp); at = (Get-Date).ToString('s'); copiedFrom = $other.Job }
                    Add-TextLine $manifestPath ([pscustomobject]$entry | ConvertTo-Json -Compress)
                    $manifest[$key] = [pscustomobject]$entry
                    $stats.copied++
                    Write-RunLog ("  [{0}/{1}] copied from {2} {3} {4}" -f $n, $todo.Count, $other.Job, $row.timestamp, $row.original)
                    continue
                }
            }
            $gaveUp = ''
            try { $got = Get-Capture $row $jd $c.target }
            catch {
                if ($_.Exception.Message -notlike 'GIVE-UP: *') { throw }
                $gaveUp = $_.Exception.Message.Substring(9)
                $got = [pscustomobject]@{ Response = [pscustomobject]@{ Status = 0; Bytes = [byte[]]@(); TooLarge = $false }; Timestamp = [string]$row.timestamp }
            }
            $r = $got.Response
            $entry = [ordered]@{ url = [string]$row.original; timestamp = [string]$row.timestamp; status = ''; http = [int]$r.Status; bytes = 0; sha256 = ''; path = ''; mimetype = [string]$row.mimetype; capture = $got.Timestamp; at = (Get-Date).ToString('s') }
            if ($got.OutOfScope) { $entry.status = 'out-of-scope' }
            elseif ($r.Status -eq 200 -and $r.TooLarge) { $entry.status = 'too-large' }
            elseif ($r.Status -eq 200) {
                $rel = Get-SaveRelativePath $row.original $row.timestamp $row.mimetype
                Save-BytesAtomically (Join-Path $jobDir $rel) $r.Bytes
                $entry.status = 'saved'; $entry.bytes = $r.Bytes.Length; $entry.sha256 = Get-Sha256Hex $r.Bytes; $entry.path = $rel
            }
            elseif ($r.Status -eq 404 -or $r.Status -eq 410) { $entry.status = 'missing' }
            elseif ($r.Status -eq 403 -or $r.Status -eq 451) { $entry.status = 'blocked' }
            else {
                # Tried again next run; after 3 runs with no answer it is left alone ('failed').
                $entry.status = 'error'
                $entry['errors'] = $prevErrors + 1
                if ($gaveUp) { $entry['reason'] = $gaveUp }
                if ($entry.errors -ge 3) { $entry.status = 'failed' }
            }
            Add-TextLine $manifestPath ([pscustomobject]$entry | ConvertTo-Json -Compress)
            $manifest[$key] = [pscustomobject]$entry
            if ($entry.status -eq 'saved') { $stats.fetched++; $stats.bytes += $entry.bytes } else { $stats.failed++ }
            Write-RunLog ("  [{0}/{1}] {2} {3} {4}" -f $n, $todo.Count, $entry.status, $row.timestamp, $row.original)
        }
    } finally {
        $saved = Write-JobIndexCsv $jobDir $manifest $null $false
        Write-RunLog ("job {0}: {1} pages saved in total (index.csv)" -f $jobInfo.Name, $saved)
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------------------------

function Invoke-Main {
    if (-not $Root) { $script:RootDir = $PSScriptRoot } else { $script:RootDir = (Resolve-Path -LiteralPath $Root).Path }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $outRoot = Join-Path $script:RootDir 'out'
    $script:StateDir = Join-Path $outRoot '_state'
    $logDir = Join-Path $outRoot '_logs'
    foreach ($d in @($outRoot, $script:StateDir, $logDir)) { if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null } }
    $script:LogPath = Join-Path $logDir ('run-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '.log')
    $script:Config = Read-Config (Join-Path $script:RootDir 'wayback-fetch.config.json')
    if ($DelaySeconds -ge 0) { $script:Config.delaySeconds = $DelaySeconds }
    if ($DailyCap -ge 0) { $script:Config.dailyRequestCap = $DailyCap }
    if ([double]$script:Config.delaySeconds -lt 1 -and $ArchiveBase -match '^https://web\.archive\.org') {
        $script:Config.delaySeconds = 1.0   # never faster than one request a second against the real archive
    }

    # One run at a time: a second double-click must not double the request rate.
    $lockPath = Join-Path $outRoot '_state\run.lock'
    try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { Write-RunLog 'Another wayback-fetch run is already going in this folder. Close it first, or wait for it to finish.'; return 3 }

    $stats = [pscustomobject]@{ jobs = 0; indexRequests = 0; indexed = 0; fetched = 0; skipped = 0; failed = 0; copied = 0; bytes = 0 }
    $started = Get-Date
    $exit = 0
    $stopReason = ''
    try {
        Write-RunLog ("wayback-fetch: archive {0}, {1} s between requests, daily cap {2} ({3} used today)" -f $ArchiveBase, $script:Config.delaySeconds, $script:Config.dailyRequestCap, (Get-DailyCount))
        $files = @()
        if ($Job.Count -gt 0) { $files = @($Job | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }) }
        else {
            $jobsDir = Join-Path $script:RootDir 'jobs'
            if (Test-Path -LiteralPath $jobsDir) { $files = @(Get-ChildItem -LiteralPath $jobsDir -Filter '*.json' | Sort-Object Name | ForEach-Object { $_.FullName }) }
        }
        if ($files.Count -eq 0) { Write-RunLog 'No jobs found (jobs\*.json).'; $exit = 1 }
        foreach ($f in $files) {
            try { $info = Read-Job $f }
            catch { Write-RunLog ("job file {0} is not valid: {1}" -f $f, $_.Exception.Message); $stats.failed++; $exit = 1; continue }
            $stats.jobs++
            $null = Invoke-Job $info $outRoot $stats
        }
    } catch {
        if ($_.Exception.Message -like 'STOP-RUN: *') { $stopReason = $_.Exception.Message.Substring(10); Write-RunLog ('Stopped: ' + $stopReason) }
        else { Write-RunLog ('Error: ' + $_.Exception.Message + ' ' + $_.InvocationInfo.PositionMessage); $exit = 1 }
    } finally {
        $lock.Dispose()
        $mins = ((Get-Date) - $started).TotalMinutes
        $state = 'finished'
        if ($stopReason) { $state = 'stopped early' } elseif ($exit -ne 0) { $state = 'finished with errors' }
        Write-RunLog ("SUMMARY {0}: jobs={1} indexed={2} fetched={3} skipped={4} failed={5} copied={12} MB={6:0.0} requests={7} today={8}/{9} minutes={10:0.0} log={11}" -f `
            $state, $stats.jobs, $stats.indexed, $stats.fetched, $stats.skipped, $stats.failed, ($stats.bytes / 1MB), $script:RequestsThisRun, (Get-DailyCount), $script:Config.dailyRequestCap, $mins, $script:LogPath, $stats.copied)
    }
    return $exit
}

# Dot-sourcing (tests) only loads the functions.
if ($MyInvocation.InvocationName -ne '.') {
    $code = Invoke-Main
    exit $code
}
