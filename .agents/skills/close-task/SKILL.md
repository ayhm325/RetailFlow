---
name: close-task
description: Close a RetailFlow task with tests, traceability, documentation synchronization, evidence, and a handoff for the next AI session.
---
# Close a RetailFlow task

1. Inspect `git diff`, `git status`, changed files, and the requested scope. Check for accidental unrelated changes.
2. Run relevant build, unit, integration, architecture, security, and browser tests available for the changed area. Record exact commands and outputs.
3. Explicitly list validation not run, environmental blockers, flaky/skipped tests, and residual risks.
4. Update affected `doc/` chapters, ADR references (`doc/02-architecture/adr/`), API/OpenAPI docs, diagrams, schema/data dictionary, `doc/09-traceability/Feature-Code-Test-Matrix.md`, and `doc/10-open-items/Production-Gaps-and-Decision-Backlog.md` only when verified facts changed.
5. Mark a finding Closed/Verified only when the closure evidence is recorded and independently reproducible.
6. Do not commit, push, merge, or deploy unless explicitly authorized.
7. Return the required task report from root `AGENTS.md`.
