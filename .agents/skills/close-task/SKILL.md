---
name: close-task
description: Close a RetailFlow task with tests, traceability, documentation synchronization, evidence, and a handoff for the next AI session.
---
# Close a RetailFlow task

1. Inspect `git diff`, `git status`, changed files, and the requested scope. Check for accidental unrelated changes.
2. Run relevant build, unit, integration, architecture, security, and browser tests available for the changed area. Record exact commands and outputs.
3. Explicitly list validation not run, environmental blockers, flaky/skipped tests, and residual risks.
4. Update affected requirements, AC, BR, use cases, ADR references, API/OpenAPI, diagrams, schema/data dictionary, and audit finding records.
5. Update `CURRENT-STATE.md` only when verified current facts changed. Update `ACTIVE-WORK.md` with remaining work and next task.
6. Append a dated entry to `WORK-LOG.md`. Never rewrite prior entries.
7. Write `AI-HANDOFF.md` for the next session: exact task, files, IDs, branch/commit, test status, decisions awaiting owner, and next command/action.
8. Mark a finding Closed/Verified only when the closure evidence is recorded and independently reproducible.
9. Do not commit, push, merge, or deploy unless explicitly authorized.
10. Return the required task report from root `AGENTS.md`.
