#requires -Version 5.1
<#
.SYNOPSIS
    Build scripts/ready.json from real unmerged, issue-linked GitHub branches.
.DESCRIPTION
    Read-only GitHub discovery. Writes one local JSON manifest; creates NO issues,
    commits, branches, PRs, or merges. Excludes previously submitted branch heads,
    branches behind main, missing diffs, and closed/missing issue references.
.EXAMPLE
    .\scripts\Build-InterLanReadyQueue.ps1
#>
[CmdletBinding()]
param(
    [string] $Repo = 'The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL',
    [string] $QueuePath = (Join-Path $PSScriptRoot 'ready.json'),
    [ValidateRange(1,5000)][int] $MaxCandidates = 850
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required. Run gh auth login first.'
}

function Invoke-GhLines {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)
    $lines = @(& gh @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed: $($lines -join [Environment]::NewLine)"
    }
    return $lines
}

function Invoke-GhObject {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)
    $lines = @(Invoke-GhLines -Arguments $Arguments)
    return (ConvertFrom-Json -InputObject ($lines -join [Environment]::NewLine))
}

$login = (@(Invoke-GhLines -Arguments @('api', 'user', '--jq', '.login')) -join '').Trim()
if ($login -cne 'Kirch-Nairu') {
    throw "Authenticated as '$login'. Switch gh to Kirch-Nairu before preparing the qualifying PR queue."
}

# Include CLOSED and MERGED PRs. Reusing an already submitted branch does
# not create independent engineering work.
$knownHeads = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$headNames = @(Invoke-GhLines -Arguments @(
    'pr', 'list', '--repo', $Repo, '--state', 'all',
    '--limit', '10000', '--json', 'headRefName', '--jq', '.[].headRefName'
))
foreach ($headName in $headNames) {
    $name = [string] $headName
    if (-not [string]::IsNullOrWhiteSpace($name)) {
        [void] $knownHeads.Add($name.Trim())
    }
}

$branches = @(Invoke-GhLines -Arguments @(
    'api', '--paginate', "repos/$Repo/branches?per_page=100",
    '--jq', '.[].name'
))
$usedIssues = New-Object 'System.Collections.Generic.HashSet[int]'
$entries = New-Object 'System.Collections.Generic.List[object]'
$examined = 0
$excluded = 0

foreach ($branchName in $branches) {
    $branch = ([string] $branchName).Trim()
    if ([string]::IsNullOrWhiteSpace($branch) -or $branch -eq 'main') {
        continue
    }
    $examined++
    if ($knownHeads.Contains($branch)) {
        $excluded++
        continue
    }

    # Encode refs containing slashes for comparison URL routing.
    $encodedBranch = [Uri]::EscapeDataString($branch)
    try {
        $compare = Invoke-GhObject -Arguments @(
            'api', "repos/$Repo/compare/main...$encodedBranch"
        )
    }
    catch {
        Write-Warning "Skipping '$branch': cannot compare to main. $($_.Exception.Message)"
        $excluded++
        continue
    }
    $changed = @($compare.files | Where-Object {
        $null -ne $_ -and $_.PSObject.Properties.Name -contains 'filename'
    })
    if ([int] $compare.ahead_by -le 0 -or
        [int] $compare.behind_by -ne 0 -or
        $changed.Count -eq 0) {
        $excluded++
        continue
    }

    $sourceCommits = @($compare.commits | Where-Object {
        $null -ne $_ -and $_.PSObject.Properties.Name -contains 'sha'
    })
    if ($sourceCommits.Count -eq 0) {
        $excluded++
        continue
    }
    $last = $sourceCommits[$sourceCommits.Count - 1]
    $subject = ([string] $last.commit.message).Split([char] 10)[0].Trim()
    $issueMatch = [regex]::Match($subject, '\(#(?<number>[0-9]+)\)')
    if (-not $issueMatch.Success) {
        Write-Warning "Skipping '$branch': newest commit subject has no linked (#issue) reference."
        $excluded++
        continue
    }
    $issueNumber = [int] $issueMatch.Groups['number'].Value
    if ($usedIssues.Contains($issueNumber)) {
        Write-Warning "Skipping '$branch': issue #$issueNumber is already represented in queue."
        $excluded++
        continue
    }

    try {
        $issue = Invoke-GhObject -Arguments @('api', "repos/$Repo/issues/$issueNumber")
    }
    catch {
        Write-Warning "Skipping '$branch': issue #$issueNumber was not found."
        $excluded++
        continue
    }
    $isPr = $issue.PSObject.Properties.Name -contains 'pull_request'
    if ($issue.state -ne 'open' -or $isPr) {
        $excluded++
        continue
    }

    [void] $usedIssues.Add($issueNumber)
    $paths = @($changed | ForEach-Object { [string] $_.filename })
    $entry = [pscustomobject] @{
        issue = $issueNumber
        branch = $branch
        title = $subject
        summary = ("Real source head {0}; changed paths: {1}. Review authorization, compatibility and code before merge." -f $last.sha, ($paths -join ', '))
        headSha = [string] $last.sha
    }
    [void] $entries.Add($entry)
    Write-Host "READY: issue #$issueNumber / $branch / $($changed.Count) files"
    if ($entries.Count -ge $MaxCandidates) {
        break
    }
}

$parent = Split-Path -Parent $QueuePath
if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}
# Explicit zero-candidate JSON array, not null, so PowerShell's runner handles it.
$json = if ($entries.Count -eq 0) {
    '[]'
}
else {
    ConvertTo-Json -InputObject ($entries.ToArray()) -Depth 6
}
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($QueuePath),
    ($json + [Environment]::NewLine),
    (New-Object System.Text.UTF8Encoding($false))
)
Write-Host ("Prepared {0} real candidate(s) from {1} branches; excluded {2}." -f $entries.Count, $examined, $excluded)
Write-Host "Queue file: $([System.IO.Path]::GetFullPath($QueuePath))"
if ($entries.Count -eq 0) {
    Write-Warning 'No new eligible source branches. The batch runner will stop safely until new source commits are pushed.'
}
