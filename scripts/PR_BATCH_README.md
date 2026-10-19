# INTER-LAN source-backed PR batch runner

The script at [Invoke-InterLanPrBatch.ps1](Invoke-InterLanPrBatch.ps1) executes a **bounded sequential loop** over submitted GitHub issues and distinct branches. This is a repository maintenance utility, not an agent and not a promise that 850 independently useful changes are available.

## Requirements

- PowerShell 5.1+ or PowerShell 7; GitHub CLI `gh`.
- Sign in via `gh auth login`; switch to the **Kirch-Nairu** account before executing.
- Each queue entry must correspond to an existing, **open** GitHub issue, a distinct pushed branch, at least one source commit ahead of current `main`, and at least one actual changed file compared with `main`.
- Each feature/documentation delta must have a useful independently describable purpose. Do **not** split one README wording tweak into dozens of counter PRs or reuse a previously merged Jane/Nox/Lucien branch.
- No CI or tests are run; skipping checks is not evidence that a release is safe.

## Existing ready.json and automatic discovery

The repository ships scripts/ready.json, initially containing []. This file exists after pulling the latest main. The discovery script Build-InterLanReadyQueue.ps1 refreshes it using **real** unsubmitted GitHub branches that have a current nonempty diff, a linked open issue, and no previously submitted PR. It creates **zero GitHub issues, branches, commits or PRs**.

### First: populate and preview

~~~powershell
Set-Location "$HOME\Dev\INTER-LAN"
git pull --ff-only origin main
.\scripts\Build-InterLanReadyQueue.ps1
.\scripts\Invoke-InterLanPrBatch.ps1 -QueuePath .\scripts\ready.json -Target 850
~~~

The queue generator checks the actual GitHub branch list and compares every possible new source branch to the latest main. Branches already submitted as PRs (open or merged), behind main, closed issues, and branches with no genuine changed files are excluded. An empty queue is a normal result if the source workers have not pushed distinct new work.

### JSON queue format

~~~json
[
  {
    "issue": 123,
    "branch": "KIRCH-INTERLAN-123-ACTUAL-SOURCE-FIX",
    "title": "fix(web): correct a confirmed session bug (#123)",
    "summary": "Concrete changed behavior and affected source paths",
    "headSha": "source-SHA-when-discovered"
  }
]
~~~

These sample values are illustrative, not existing GitHub work. Workers can produce additional real issues and branches, then refresh the queue. Source author credit remains with the actual worker.

### When actual candidates have been reviewed

Open Kirch-owned PRs but do not merge:

~~~powershell
.\scripts\Invoke-InterLanPrBatch.ps1 -QueuePath .\scripts\ready.json -Target 850 -Execute
~~~

Open and request sequential normal merges with a source-head SHA lease:

~~~powershell
.\scripts\Invoke-InterLanPrBatch.ps1 -QueuePath .\scripts\ready.json -Target 850 -Execute -Merge
~~~

850 is a ceiling. An empty queue creates zero PRs and does not repeatedly change README. The batch runner does not run CI or tests. No runtime or security acceptance is implied by a merge.

## Relationship to GitHub achievements

PRs are attributed to the GitHub account that creates them. Only **merged qualifying** PRs potentially contribute to Pull Shark. The script verifies creator `Kirch-Nairu` and records actual merge responses, but GitHub achievement eligibility is not the same as an arbitrary numeric search count. A documentation diff can be legitimate without creating hundreds of near-identical one-line PRs.

## Parallel work coordination

[Campaign issue #16](../../../issues/16) tracks shared authority. Lucien's [PR queue issue #63](../../../issues/63) collects new candidate branches and their exact SHAs; Jane's backend [issue #35](../../../issues/35) and Nox's native parent [issue #19](../../../issues/19) track distinct substantive code. An assigned GitHub issue **does not automatically start an external agent**. Someone must execute each coding session and publish changes before the runner can process them.

**Verification:** This runner has not been executed against GitHub CLI as part of this code-first dispatch; build/tests/CI are NOT RUN.
