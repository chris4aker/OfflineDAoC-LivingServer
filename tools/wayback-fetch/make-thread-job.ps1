# make-thread-job.ps1 - builds a thread job (jobs\a2-raid-threads.json, a3-build-threads.json or a4-talk-threads.json)
# from forum listings that wayback-fetch already saved.
#
# Runs on Windows PowerShell 5.1, offline: it reads only out\<job>\ (default: out\a-raid-sizes\ and out\4-vocab\)
# and makes no requests. MAKE-THREAD-JOBS.cmd runs all three presets; then RUN-WAYBACK-FETCH.cmd fetches the threads.
#
# For each saved listing page it reads the thread links (vBulletin showthread.php?t= / threadid=, Allakhazam
# forum.html?mid=, VN Boards message.asp?topic=), keeps the threads whose TITLE matches the preset (raid: raid
# talk; builds: specs, templates, realm abilities; talk: LFG, trade, guild recruitment, RvR calls), and writes one
# exact target per thread (the link as the listing had it). Run it again after a fetch run: threads whose exact
# lookup found nothing (their archived URLs carry another session id, for example) move into a few id-filter
# targets per forum instead.
#
# Parameter names differ from wayback-fetch.ps1's on purpose: dot-sourcing it binds its own parameters here.
param(
    [string]$Folder = '',
    [ValidateSet('raid', 'builds', 'talk')]
    [string]$Preset = 'raid',
    [string[]]$FromJobs = @('a-raid-sizes', '4-vocab'),
    # Empty: the preset's job (a2-raid-threads, a3-build-threads, a4-talk-threads).
    [string]$JobName = '',
    [int]$MaxThreads = 150,
    [int]$BatchSize = 25,
    [string]$From = '2002',
    [string]$To = '2005'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$opts = @{ Folder = $Folder; Preset = $Preset; FromJobs = $FromJobs; JobName = $JobName; MaxThreads = $MaxThreads; BatchSize = $BatchSize; From = $From; To = $To }
. (Join-Path $here 'wayback-fetch.ps1')
if (-not $opts.Folder) { $opts.Folder = $here }

# Forum software of the era: where its thread links live and which parameter is the thread id.
$script:ThreadSites = @(
    [pscustomobject]@{ Name = 'vBulletin'; PathRx = '/showthread\.php$'; IdParams = @('t', 'threadid') },
    [pscustomobject]@{ Name = 'Allakhazam'; PathRx = '/forum\.html$'; IdParams = @('mid') },
    [pscustomobject]@{ Name = 'VN Boards'; PathRx = '/message\.asp$'; IdParams = @('topic') }
)

# Which thread titles each preset keeps: Rx in any case, CapsRx only in capitals (DF, RA, LFG...).
$script:Presets = @{
    raid = [pscustomobject]@{
        Job = 'a2-raid-threads'
        Purpose = 'raid talk and raid sizes: dragons, Darkness Falls, ToA encounters, epic raids'
        Rx = '(?i)\b(dragons?|gjalp\w*|golestandt|cuuldurach|legion|govannon|apoc(alypse)?|olcasgean|tuscar|caer\s*sidi|galladoria|epics?|raid(s|ing|ers?)?|darkness\s+falls)\b'
        CapsRx = '\bDF\b'
    }
    builds = [pscustomobject]@{
        Job = 'a3-build-threads'
        Purpose = 'real era builds: specs, templates, respecs, realm ability picks, spellcrafting'
        Rx = '(?i)\b(specs?|spec(c?ed|ing)|respecs?|templates?|builds?|realm\s*abilit\w*|spellcraft\w*|point\s+spread)\b'
        CapsRx = '\b(RAs?|RR\s?\d+|SC''?d?|SC)\b'
    }
    talk = [pscustomobject]@{
        Job = 'a4-talk-threads'
        Purpose = 'how players talked: LFG, trade, guild recruitment, RvR calls and server community'
        Rx = '(?i)\b(lfg|lf\s*(grp|group|guild)|looking\s+for|wts|wtb|wtt|pst|recruit\w*|guilds?|rvr|zerg\w*|stealth\w*|buff\s*bots?|gank\w*|emain|odin''?s|hadrian''?s|relics?|keep\s+takes?|realm\s+war|server\s+(event|community))\b'
        CapsRx = '\b(INC|LFM|LFG|WTS|WTB)\b'
    }
}
function Test-ThreadTitle([string]$title, [string]$preset) {
    $p = $script:Presets[$preset]
    if ([regex]::IsMatch($title, $p.Rx)) { return $true }
    return [regex]::IsMatch($title, $p.CapsRx)
}

function Get-QueryParam([string]$query, [string]$name) {
    $m = [regex]::Match($query, '(?i)(?:^\?|[?&])' + [regex]::Escape($name) + '=([^&#]*)')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

# A link to page 2+ of a thread, or a "go to last post" link: not the thread's own address.
function Test-SecondaryLink([string]$query) {
    return [regex]::IsMatch($query, '(?i)[?&](goto|p)=|[?&](page|pagenumber)=([2-9]|\d{2,})')
}

function Get-LinkText([string]$inner) {
    $t = [regex]::Replace($inner, '(?s)<[^>]*>', ' ')
    $t = [System.Net.WebUtility]::HtmlDecode($t)
    return ([regex]::Replace($t, '\s+', ' ')).Trim()
}

function Test-TitleText([string]$text) {
    if ($text.Length -lt 4) { return $false }
    if ($text -match '^[\d\s.,:;<>\-_|\[\]()\u00ab\u00bb]+$') { return $false }
    return $text -notmatch '(?i)^(last page|first unread|go to|new posts?|next|prev(ious)?)\b'
}

# All thread links in one listing page. Returns objects: Site, Host, Path, IdParam, Id, Url (no fragment), Text, Secondary.
function Get-ThreadLinks([string]$html, [string]$baseUrl) {
    $links = New-Object System.Collections.Generic.List[object]
    $base = New-Object Uri $baseUrl
    $baseHost = Get-NormalizedHost $baseUrl
    $rx = '(?is)<a\s[^>]*?href\s*=\s*(?:"([^"]*)"|''([^'']*)''|([^\s>]+))[^>]*>(.*?)</a\s*>'
    foreach ($m in [regex]::Matches($html, $rx)) {
        $href = $m.Groups[1].Value + $m.Groups[2].Value + $m.Groups[3].Value
        $href = [System.Net.WebUtility]::HtmlDecode($href).Trim()
        if (-not $href -or $href -match '^(?i)(javascript|mailto):' -or $href.StartsWith('#')) { continue }
        try { $abs = New-Object Uri ($base, $href) } catch { continue }
        if ($abs.Scheme -ne 'http' -and $abs.Scheme -ne 'https') { continue }
        if ((Get-NormalizedHost $abs.AbsoluteUri) -ne $baseHost) { continue }
        foreach ($site in $script:ThreadSites) {
            if ($abs.AbsolutePath -notmatch ('(?i)' + $site.PathRx)) { continue }
            foreach ($p in $site.IdParams) {
                $id = Get-QueryParam $abs.Query $p
                if ($id -match '^\d+$') {
                    $links.Add([pscustomobject]@{
                        Site = $site.Name; Host = $baseHost; Path = $abs.AbsolutePath; IdParam = $p; Id = $id
                        Url = $abs.GetLeftPart([UriPartial]::Query); Text = (Get-LinkText $m.Groups[4].Value)
                        Secondary = (Test-SecondaryLink $abs.Query)
                    })
                    break
                }
            }
            break
        }
    }
    return ,$links
}

# Merges the links of many listings into one record per thread: its first good title and its own address.
function Merge-Threads($found, $links, [string]$listingUrl, [string]$listingTs) {
    foreach ($l in $links) {
        $key = $l.Host + '|' + $l.Site + '|' + $l.Id
        if (-not $found.Contains($key)) {
            $found[$key] = [pscustomobject]@{ Site = $l.Site; Host = $l.Host; Path = $l.Path; IdParam = $l.IdParam; Id = $l.Id; Url = ''; Title = ''; Listing = $listingUrl; ListingTimestamp = $listingTs }
        }
        $t = $found[$key]
        if (-not $t.Url -and -not $l.Secondary) { $t.Url = $l.Url; $t.Path = $l.Path; $t.IdParam = $l.IdParam }
        if (-not $t.Title -and -not $l.Secondary -and (Test-TitleText $l.Text)) { $t.Title = $l.Text }
    }
}

function Get-UrlWithoutScheme([string]$url) { return ($url -replace '^(?i)https?://', '') }

function New-ExactTarget($thread) {
    $title = $thread.Title
    if ($title.Length -gt 120) { $title = $title.Substring(0, 117) + '...' }
    return [ordered]@{
        url = (Get-UrlWithoutScheme $thread.Url); matchType = 'exact'; guess = $false
        note = ('{0} thread {1}: "{2}" (listed on {3})' -f $thread.Site, $thread.Id, $title, $thread.Listing)
        cdxPageSize = 10; cdxMaxPages = 1; maxCaptures = 1
    }
}

# One id-filter target per forum and batch: the archive's index is scanned once for many threads.
function New-FilterTargets($threads, [int]$batch) {
    $out = New-Object System.Collections.Generic.List[object]
    $groups = $threads | Group-Object { $_.Host + $_.Path }
    foreach ($g in $groups) {
        $first = $g.Group[0]
        $site = $script:ThreadSites | Where-Object { $_.Name -eq $first.Site } | Select-Object -First 1
        $params = '(' + (($site.IdParams | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')'
        $ids = @($g.Group | ForEach-Object { $_.Id })
        for ($i = 0; $i -lt $ids.Count; $i += $batch) {
            $chunk = @($ids[$i..([Math]::Min($ids.Count, $i + $batch) - 1)])
            $out.Add([ordered]@{
                url = $first.Host + $first.Path; matchType = 'prefix'; guess = $false
                note = ('{0} threads whose exact link found no capture: {1}' -f $first.Site, ($chunk -join ', '))
                cdxFilters = @(('urlkey:.*[?&]' + $params + '=(' + ($chunk -join '|') + ')(&.*)?'), '!urlkey:.*[?&](page|pagenumber)=([2-9]|[0-9][0-9]+).*')
                exclude = @('(?i)[?&](goto|p)=')
                cdxPageSize = 200; cdxMaxPages = 2; maxCaptures = $chunk.Count
            })
        }
    }
    return ,$out
}

# Did an earlier fetch run finish this exact target's index with no rows at all?
function Test-ExactFoundNothing([string]$jobDir, $jd, $target) {
    $key = Get-TargetKey $jd ([pscustomobject]$target)
    $idx = Join-Path $jobDir 'index'
    if (-not (Test-Path -LiteralPath $idx)) { return $false }
    foreach ($f in @(Get-ChildItem -LiteralPath $idx -Filter ('target*-' + $key + '.state.json'))) {
        $s = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([bool](Get-Prop $s 'done' $false) -and [int](Get-Prop $s 'rows' 0) -eq 0) { return $true }
    }
    return $false
}

function Invoke-MakeThreadJob {
    $preset = $script:Presets[$opts.Preset]
    if (-not $opts.JobName) { $opts.JobName = $preset.Job }
    $outRoot = Join-Path $opts.Folder 'out'
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $found = [ordered]@{}
    $pages = 0
    foreach ($src in $opts.FromJobs) {
        $dir = Join-Path $outRoot $src
        $manifest = Read-Manifest (Join-Path $dir 'manifest.jsonl')
        foreach ($e in $manifest.Values) {
            if ([string](Get-Prop $e 'status' '') -ne 'saved') { continue }
            $file = Join-Path $dir ([string]$e.path)
            if (-not (Test-Path -LiteralPath $file)) { continue }
            $html = $latin1.GetString([IO.File]::ReadAllBytes($file))
            $pages++
            Merge-Threads $found (Get-ThreadLinks $html ([string]$e.url)) ([string]$e.url) ([string]$e.timestamp)
        }
    }
    $all = @($found.Values | Where-Object { $_.Url -and $_.Title })
    $raid = @($all | Where-Object { Test-ThreadTitle $_.Title $opts.Preset } | Sort-Object @{ e = { $_.Host } }, @{ e = { [long]$_.Id } })
    $dropped = 0
    if ($raid.Count -gt $opts.MaxThreads) { $dropped = $raid.Count - $opts.MaxThreads; $raid = @($raid[0..($opts.MaxThreads - 1)]) }

    $jd = [ordered]@{
        name = $opts.JobName
        note = ('Forum threads 2002-2005 for {0}: the threads whose titles match the "{1}" preset, found in the saved listings of {2} by make-thread-job.ps1. Generated on this PC; never committed (third-party titles).' -f $preset.Purpose, $opts.Preset, ($opts.FromJobs -join ', '))
        from = $opts.From; to = $opts.To; mimetypes = @('text/html')
        dedupeKey = '[?&](?:t|threadid|mid|topic)=(\d+)(?:&|$)'
        generated = (Get-Date).ToString('s')
        targets = $null
        threads = $null
    }
    $jobDir = Join-Path $outRoot $opts.JobName
    $targets = New-Object System.Collections.Generic.List[object]
    $fallback = New-Object System.Collections.Generic.List[object]
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($t in $raid) {
        $et = New-ExactTarget $t
        $isFallback = Test-ExactFoundNothing $jobDir $jd $et
        if ($isFallback) { $fallback.Add($t) } else { $targets.Add($et) }
        $list.Add([ordered]@{ site = $t.Site; id = $t.Id; title = $t.Title; url = $t.Url; listing = $t.Listing; listingTimestamp = $t.ListingTimestamp; lookup = $(if ($isFallback) { 'id-filter' } else { 'exact' }) })
    }
    foreach ($ft in (New-FilterTargets $fallback $opts.BatchSize)) { $targets.Add($ft) }
    $jd.targets = $targets
    $jd.threads = $list
    $jd.maxCaptures = $raid.Count

    $jobsDir = Join-Path $opts.Folder 'jobs'
    if (-not (Test-Path -LiteralPath $jobsDir)) { New-Item -ItemType Directory -Path $jobsDir -Force | Out-Null }
    $jobPath = Join-Path $jobsDir ($opts.JobName + '.json')
    if ($raid.Count -eq 0) {
        if (Test-Path -LiteralPath $jobPath) { Remove-Item -LiteralPath $jobPath -Force }
        Write-Host ("make-thread-job {0}: {1} listing pages, {2} threads, none with a matching title; no job written." -f $opts.Preset, $pages, $all.Count)
        return 1
    }
    $json = ConvertTo-Json -InputObject $jd -Depth 8
    [IO.File]::WriteAllText($jobPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("make-thread-job " + $opts.Preset + ": {0} listing pages, {1} threads, {2} matching threads ({3} exact lookups, {4} by id filter in {5} targets{6}). Wrote {7}" -f `
        $pages, $all.Count, $raid.Count, ($raid.Count - $fallback.Count), $fallback.Count, ($targets.Count - ($raid.Count - $fallback.Count)), $(if ($dropped) { ", $dropped over -MaxThreads left out" } else { '' }), $jobPath)
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    $code = Invoke-MakeThreadJob
    exit $code
}
