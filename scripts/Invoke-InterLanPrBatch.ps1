#requires -Version 5.1
<#
.SYNOPSIS
    Submit issue-linked PRs from a curated queue of independent branch changes.
.DESCRIPTION
    This runner never invents source changes, issues or README edits. It stops
    when the queue is empty or the requested limit is reached. It checks the
    authenticated account, issue validity, changed files and existing PRs.
    Dry-run is the default. -Execute creates PRs. -Merge additionally requests
    normal merge commits with a lease on the exact PR head. No tests/CI run here.
.EXAMPLE
    pwsh ./scripts/Invoke-InterLanPrBatch.ps1 -QueuePath ./scripts/pr-queue.example.json -Target 850
.EXAMPLE
    pwsh ./scripts/Invoke-InterLanPrBatch.ps1 -QueuePath ./scripts/ready.json -Target 850 -Execute -Merge
#>
[CmdletBinding()]
param(
    [string] $Repo = 'The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL',
    [string] $QueuePath = (Join-Path $PSScriptRoot 'pr-queue.example.json'),
    [ValidateRange(1,5000)][int] $Target = 850,
    [switch] $Execute,
    [switch] $Merge
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Merge -and -not $Execute) {
    throw 'To request merges, specify -Execute -Merge.'
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'Install GitHub CLI and authenticate with gh auth login.'
}
if (-not (Test-Path -LiteralPath $QueuePath -PathType Leaf)) {
    throw "Queue file not found: $QueuePath"
}

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)
    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ("GitHub CLI failed: gh {0}: {1}" -f ($Arguments -join ' '), ($output | Out-String))
    }
    $text = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "GitHub CLI returned empty JSON for gh $($Arguments -join ' ')"
    }
    return ($text | ConvertFrom-Json)
}

function Get-KirchMergedPrCount {
    $result = Invoke-GhJson -Arguments @(
        'api', '--method', 'GET', 'search/issues',
        '-f', 'q=is:pr is:merged author:Kirch-Nairu', '-f', 'per_page=1'
    )
    return [int] $result.total_count
}

$identity = Invoke-GhJson -Arguments @('api', 'user')
if ([string] $identity.login -cne 'Kirch-Nairu') {
    throw "The authenticated account is $($identity.login); only Kirch-Nairu may open the qualifying PR."
}

$queue = @(Get-Content -LiteralPath $QueuePath -Raw | ConvertFrom-Json)
if ($queue.Count -eq 0) {
    Write-Warning 'Empty queue. No README changes, commits, issues or PRs will be fabricated.'
    exit 0
}

$startCount = Get-KirchMergedPrCount
$seenIssues = New-Object 'System.Collections.Generic.HashSet[int]'
$seenBranches = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$created = 0
$merged = 0
$preview = 0
$skipped = 0
$index = 0
Write-Host "Authenticated as $($identity.login); initial author-merged PR count: $startCount"
Write-Host "Target new merges: $Target; execute: $Execute; merge: $Merge; queued entries: $($queue.Count)"

while ($index -lt $queue.Count -and $merged -lt $Target) {
    $item = $queue[$index]
    $index++
    $issueNumber = [int] $item.issue
    $branch = [string] $item.branch
    $title = [string] $item.title
    $summary = [string] $item.summary
    if ($issueNumber -le 0 -or
        [string]::IsNullOrWhiteSpace($branch) -or
        [string]::IsNullOrWhiteSpace($title) -or
        [string]::IsNullOrWhiteSpace($summary)) {
        throw "Queue entry $index must contain issue, branch, title and substantive summary."
    }
    if ($branch -ceq 'main' -or $branch -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$') {
        throw "Invalid head branch at entry $index."
    }
    if (-not $seenIssues.Add($issueNumber) -or -not $seenBranches.Add($branch)) {
        throw "Duplicate issue or branch within queue at entry $index."
    }

    Write-Host "[$index/$($queue.Count)] Issue #$issueNumber / $branch"
    $issue = Invoke-GhJson -Arguments @('api', "repos/$Repo/issues/$issueNumber")
    $isPullRequestIssue = $issue.PSObject.Properties.Name -contains 'pull_request'
    if ($issue.state -ne 'open' -or $isPullRequestIssue) {
        Write-Warning "Skip #$issueNumber: issue closed or resolves to a pull request."
        $skipped++; continue
    }
    $compare = Invoke-GhJson -Arguments @('api', "repos/$Repo/compare/main...$branch")
    $files = @($compare.files)
    if ([int] $compare.ahead_by -le 0 -or $files.Count -eq 0) {
        Write-Warning "Skip #$issueNumber: no branch-ahead unmerged file diff."
        $skipped++; continue
    }
    $previous = @(Invoke-GhJson -Arguments @(
        'pr', 'list', '--repo', $Repo,
        '--head', $branch, '--state', 'all',
        '--limit', '100', '--json', 'number,mergedAt,state'
    ))
    if ($previous.Count -gt 0) {
        Write-Warning "Skip #$issueNumber: branch already submitted as PR #$($previous[0].number)."
        $skipped++; continue
    }
    $branchData = Invoke-GhJson -Arguments @('api', "repos/$Repo/branches/$branch")
    $headSha = [string] $branchData.commit.sha
    Write-Host "  Diff: $($files.Count) changed file(s); ahead $($compare.ahead_by); head $headSha"

    if (-not $Execute) {
        $preview++; continue
    }

    $body = @(
        "Closes #$issueNumber"
        'Refs #16'
        ''
        $summary
        ''
        "Source branch: $branch"
        "Verified PR-creation head: $headSha"
        'Source author and committer attribution must reflect actual contributors.'
        'Build, tests and CI: NOT RUN by this script. Runtime/security acceptance is not inferred.'
    ) -join [Environment]::NewLine
    $pr = Invoke-GhJson -Arguments @(
        'api', '--method', 'POST', "repos/$Repo/pulls",
        '-f', "head=$branch", '-f', 'base=main',
        '-f', "title=$title", '-f', "body=$body"
    )
    if ($pr.user.login -cne 'Kirch-Nairu') {
        throw "Unexpected GitHub PR creator $($pr.user.login); aborting."
    }
    $created++
    Write-Host "  PR #$($pr.number) opened: $($pr.html_url)"

    if ($Merge) {
        $current = Invoke-GhJson -Arguments @('api', "repos/$Repo/pulls/$($pr.number)")
        if ([string] $current.head.sha -ne $headSha) {
            throw "PR #$($pr.number) head moved. Stop; inspect new worker changes before merge."
        }
        $mergeResult = Invoke-GhJson -Arguments @(
            'api', '--method', 'PUT', "repos/$Repo/pulls/$($pr.number)/merge",
            '-f', 'merge_method=merge', '-f', "sha=$headSha"
        )
        if (-not $mergeResult.merged) {
            throw "Merge not confirmed for PR #$($pr.number); stopping."
        }
        $merged++
        Write-Host "  PR #$($pr.number) merged at $($mergeResult.sha)"
    }
}

$after = Get-KirchMergedPrCount
Write-Host "BATCH SUMMARY: opened=$created; merged=$merged; preview-ready=$preview; skipped=$skipped."
Write-Host "GitHub search author-merged count: $startCount => $after (badge eligibility can differ)."
if ($index -ge $queue.Count -and $merged -lt $Target) {
    Write-Warning "Queue exhausted: $merged / $Target requested new merges achieved. No filler PRs were made."
}
