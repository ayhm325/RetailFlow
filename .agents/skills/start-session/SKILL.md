---
name: start-session
description: Start or resume RetailFlow work safely. Load current state, verify repository reality, recover the active task and identify the next evidence-backed action before editing.
---
# Start a RetailFlow work session

1. Read root `AGENTS.md`, `docs/engineering/README.md`, then `CURRENT-STATE.md`, `ACTIVE-WORK.md`, and `AI-HANDOFF.md`.
2. Inspect Git status, current branch, HEAD SHA, remotes, and uncommitted changes. Do not reset, stash, checkout, or clean user changes.
3. Inspect repository tree and relevant source/tests/docs. Confirm whether the control files' recorded baseline still matches HEAD.
4. Read the relevant original source docs, Rev2.0 baseline chapter(s), ADR(s), audit findings, and registers. Do not load every chapter if unrelated.
5. Identify conflicts among code, tests, ADRs, requirements, and current state. Record them as explicit blockers/questions.
6. Present a concise session brief:
   - observed branch/commit/working-tree state;
   - active objective and scope;
   - applicable requirements/AC/BR/ADR IDs;
   - current implementation facts and evidence;
   - open risks/findings;
   - proposed plan and validation commands.
7. Do not edit until the task is sufficiently understood. Ask the owner if a product policy or unresolved decision blocks safe implementation.
