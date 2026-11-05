---
name: audit
description: Full audit of Claude Code Tray — build, self-test, parallel per-partition scanners, adversarial verification, and reconciliation of the roadkeep backlog (close done tasks, retire outdated ones, reword unclear ones, file confirmed findings). Use when asked to audit the repo, hunt for bugs across the codebase, or reconcile the roadmap against the code.
---

# /audit

Run the steps in order. Do not skip a step; if one cannot run, say so in the report.

## 1. Baseline checks (sequential — never two builds at once)

```
dotnet build -c Debug --nologo -warnaserror
powershell -NoProfile -Command "$p = Start-Process 'bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe' -ArgumentList '--selftest','--quick' -Wait -PassThru -RedirectStandardOutput $env:TEMP\st.log; Get-Content $env:TEMP\st.log; exit $p.ExitCode"
cd site && npm run typecheck && npm test
roadkeep lint
```

Every failure here is a finding (`path:line | … | … | the command`). UI cases
(`dotnet test tests\ClaudeTray.Cases`) are NOT run on this desk — they run in the VM.

## 2. Scan — one `scanner` per partition, all in ONE message (parallel)

Use the Agent tool with `subagent_type: "scanner"`, prompt `Partition: <paths>`:

| # | Partition |
|---|---|
| P1 | `src/Cli/` |
| P2 | `src/Ui/` |
| P3 | `src/Usage/` |
| P4 | `src/Tray/`, `src/Core/`, `src/Profiles/` |
| P5 | `src/Context/`, `tests/ClaudeTray.Cases/`, `cases/` |
| P6 | `site/src/`, `site/scripts/`, `build/`, `scripts/`, `lang/`, `.github/workflows/` |

## 3. Deduplicate

Merge the lines from step 1 and 2. Same `path:line`, or same issue at the same call site → one
line. Drop `NO FINDINGS`.

## 4. Verify findings

One `verifier` agent, prompt `MODE: FINDINGS` + the full deduplicated list. Keep only CONFIRMED.

## 5. Verify the backlog

```
roadkeep list --json
```

One `verifier` agent, prompt `MODE: ROADMAP` + that task list + the CONFIRMED findings.

## 6. Reconcile — through roadkeep only, never by editing the files

Never hand-edit `ROADMAP.md`, `CHANGELOG.md`, `IMPROVEMENTS.md` or `STRATEGY.md`.

- **ALREADY DONE** → `roadkeep ship <id> --why "<what now works, one sentence.>"`
- **OUTDATED** → `roadkeep retire <id> --reason "<why it no longer applies.>"`
  (use `--folds-into <id>` when another open line absorbs it)
- **NEEDS REWORDING** → `roadkeep restate <id> --symptom "<text>"` and/or
  `roadkeep amend <id> --why "<text>"`
- **NEW** (CONFIRMED and not covered) → first `roadkeep budget --symptom "<text>"`, then
  `roadkeep add --block <L> --ref <anchor> --symptom "<text>" --why "<text>" --section "<title>" --section-body-file <scratch file>`.
  `--ref` is mandatory here (`ref_scheme = "outline"`); find a free anchor with `roadkeep anchors`.
  Pass long text via a variable or file, never inline in PowerShell. Reuse an existing block.
- **STILL VALID** → no action.

Then `roadkeep lint`; if it reports anything, `roadkeep repair`, then `roadkeep lint` again.
Do not commit — the user decides (one task per commit, via `run-commit.cmd -m`).

## 7. Report — counts only

```
Findings: <n> confirmed, <n> false positive, <n> unverifiable
Tasks:    <n> kept, <n> closed, <n> removed, <n> rewritten, <n> added
```
