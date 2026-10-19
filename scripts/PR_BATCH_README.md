# INTER-LAN source-backed PR batch runner

The script at [Invoke-InterLanPrBatch.ps1](Invoke-InterLanPrBatch.ps1) executes a **bounded sequential loop** over submitted GitHub issues and distinct branches. This is a repository maintenance utility, not an agent and not a promise that 850 independently useful changes are available.

## Requirements

- PowerShell 5.1+ or PowerShell 7; GitHub CLI `gh`.
- Sign in via `gh auth login`; switch to the **Kirch-Nairu** account before executing.
- Each queue entry must correspond to an existing, **open** GitHub issue, a distinct pushed branch, at least one source commit ahead of current `main`, and at least one actual changed file compared with `main`.
- Each feature/documentation delta must have a useful independently describable purpose. Do **not** split one README wording tweak into dozens of counter PRs or reuse a previously merged Jane/Nox/Lucien branch.
- No CI or tests are run; skipping checks is not evidence that a release is safe.

## Queue JSON

The shipped `pr-queue.example.json` deliberately contains `[]`; an empty queue causes zero writes. Create a different file outside source control, e.g. `ready.json`, containing records **only after** a worker has finished and pushed each issue-linked change.

Example **format only**, not an assertion that these example refs exist:

```json
[
  {
    "issue": 123,
    "branch": "KIRCH-INTERLAN-123-REAL-CHANGE",
    "title": "docs(api): explain an implemented recovery behavior",
    "summary": "Documents the server behavior from current code, including a specific failure and safe recovery procedure."
  }
]
```

A real queue should contain independent entries by Jane (backend), Lucien (web/documentation), Nox (native) or Kirch. Original commit authorship stays unchanged; opening the PR as Kirch is a separate action.

## Commands

Preview only (default: no GitHub writes):

```powershell
pwsh ./scripts/Invoke-InterLanPrBatch.ps1 -QueuePath ./ready.json -Target 850
```

Open Kirch-owned PRs but leave them unmerged:

```powershell
pwsh ./scripts/Invoke-InterLanPrBatch.ps1 -QueuePath ./ready.json -Target 850 -Execute
```

Open and request normal merges for each ready entry:

```powershell
pwsh ./scripts/Invoke-InterLanPrBatch.ps1 -QueuePath ./ready.json -Target 850 -Execute -Merge
```

A merge uses an **exact source HEAD SHA** lease and GitHub's merge endpoint; if a worker pushes more commits before merge, the operation stops rather than integrating unseen work. Existing PRs, closed issues, branches with zero changed files, and already-included diffs are skipped. The runner stops at queue exhaustion or when the requested new-merge count is reached, whichever occurs first. It does not fabricate additional changes or trigger more workers.

## Relationship to GitHub achievements

PRs are attributed to the GitHub account that creates them. Only **merged qualifying** PRs potentially contribute to Pull Shark. The script verifies creator `Kirch-Nairu` and records actual merge responses, but GitHub achievement eligibility is not the same as an arbitrary numeric search count. A documentation diff can be legitimate without creating hundreds of near-identical one-line PRs.

## Parallel work coordination

[Campaign issue #16](../../../issues/16) tracks shared authority. Lucien's [PR queue issue #63](../../../issues/63) collects new candidate branches and their exact SHAs; Jane's backend [issue #35](../../../issues/35) and Nox's native parent [issue #19](../../../issues/19) track distinct substantive code. An assigned GitHub issue **does not automatically start an external agent**. Someone must execute each coding session and publish changes before the runner can process them.

**Verification:** This runner has not been executed against GitHub CLI as part of this code-first dispatch; build/tests/CI are NOT RUN.
