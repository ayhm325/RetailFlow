# RetailFlow Engineering Memory & AI Operating System

This folder is the durable, version-controlled operating memory for RetailFlow. It is designed to work with Antigravity first and remain portable to other coding agents.

## Read order for a new session
1. Repository root `AGENTS.md`.
2. `control/CURRENT-STATE.md`.
3. `control/ACTIVE-WORK.md`.
4. `control/AI-HANDOFF.md`.
5. Relevant baseline chapters, ADRs, audit findings, registers, source files, tests, and CI evidence.

## Canonical source hierarchy
- **Current implementation:** actual source code, project files, configuration, migrations, tests, and CI run evidence at a specific commit.
- **Intended product behavior:** owner-approved requirements, business rules, and acceptance criteria.
- **Architecture decisions:** ADRs with status `Accepted` only.
- **Open issues:** audit findings and open-decision register.
- **Execution continuity:** files in `control/`.
- **Technical authority:** current official documentation/standards, recorded in `references/official-sources.md`.

If sources conflict, do not silently choose one. Record the conflict, its impact, and the owner decision required.

## Status vocabulary
- `Implemented`: code exists in the Product Host or specified production component.
- `Partially implemented`: only a documented subset is implemented.
- `PoC`: implemented in an isolated experiment; not integrated into Product Host.
- `Proposed`: suggested but not owner-approved.
- `Accepted`: owner-approved decision; does not itself prove implementation.
- `Verified`: implementation and required evidence have been checked.
- `Not implemented`: inspected and absent from current scope.
- `Not verified`: a claim exists but evidence has not been checked.
- `Unknown`: insufficient evidence to classify.

## Editing rules
- Editable masters: Markdown, CSV, YAML, Mermaid, and code.
- PDF/DOCX: publication outputs; do not edit them independently as the only source.
- Keep stable IDs. Do not silently rename, recycle, or merge IDs.
- Append work-log entries; preserve decision and audit history.
- Every task close updates only the files affected, plus the handoff/control files.
- Do not generate a second competing "master" document.

## Package contents
- `control/`: current state, active work, handoff, append-only work log.
- `baseline/`: imported Rev2.0 engineering baseline source package.
- `audit/`: imported Documentation-to-Code Audit Rev2.0 and finding registers.
- `decisions/`: ADR policy, template, and local decision index.
- `registers/`: active copies/indices for decisions, evidence, and open work.
- `references/`: official tool/agent documentation and source inventory.
- `templates/`: repeatable task, finding, evidence, and change templates.
- `prompts/`: reusable prompts for clients that do not discover skills automatically.

## Current snapshot
The initial snapshot in this package was prepared against `main` at commit `04392c875c275e99f4b5ee378f63b3a8b8fc541d` on 2026-11-26. It is a starting point, not a claim that all audit findings have since been fixed. Re-check Git before relying on it.
