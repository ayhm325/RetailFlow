# RetailFlow — Repository Agent Contract

This file is the repository-wide, always-on operating contract. Keep it concise; detailed procedures live in `.agents/skills/` and project knowledge lives in `docs/engineering/`.

## 1. Mission and non-negotiable boundaries
- Work only within the user's explicitly requested scope. Do not broaden a task or start unrelated features.
- Treat this repository as a multi-tenant commerce product. Tenant isolation, authorization boundaries, data integrity, security, and auditability are release-critical.
- Preserve the existing architectural boundaries. Do not introduce cross-layer dependencies or bypass Application/Domain boundaries without an approved, documented decision.
- Do not expose, copy, or log secrets, credentials, tokens, cookies, connection strings, personal data, or production data.
- Do not modify `main`, create/push branches, open PRs, deploy, or perform destructive operations unless the user explicitly authorizes that action.

## 2. Mandatory context loading
Before planning or editing:
1. Read `docs/engineering/README.md`.
2. Read `docs/engineering/control/CURRENT-STATE.md`, `ACTIVE-WORK.md`, and `AI-HANDOFF.md`.
3. Read the relevant baseline chapters, requirements, business rules, ADRs, registers, audit findings, and evidence.
4. Inspect the actual repository tree, Git status, current branch, HEAD commit, and relevant source/tests. Never rely only on documentation or a previous chat summary.
5. If these control files conflict with Git or code, record the discrepancy; do not silently choose one.

## 3. Evidence and truth
- Distinguish **Current/Implemented**, **PoC**, **Proposed/Target**, **Partially implemented**, **Verified**, **Not verified**, and **Unknown**.
- Documentation does not prove implementation. A PoC does not prove Product Host integration. A passing test does not prove production readiness.
- Current behavior must be established from source code, configuration, migrations, tests, and CI evidence.
- Desired behavior must be established from approved requirements and accepted ADRs.
- Proposed ADRs, drafts, target diagrams, and roadmap items are not approved implementation instructions.
- Never invent requirements, business rules, performance targets, owners, evidence, test results, or approval.

## 4. Decisions
- Do not change an Accepted ADR or an approved business/security invariant silently.
- For a material architectural/security/data/operational decision, create an ADR using `docs/engineering/decisions/ADR_TEMPLATE.md`.
- The agent may research and recommend; only the project owner may move a decision to `Accepted`.
- Preserve history. Supersede decisions by a new ADR that references the old one; do not erase the decision trail.
- Record source URLs, versions/dates, alternatives, trade-offs, consequences, migration/rollback, and verification.

## 5. Engineering workflow
Use the `start-session` skill before substantial work and `close-task` before declaring completion.
- Plan first for cross-cutting, security-sensitive, data, architectural, or multi-file work.
- Implement the smallest coherent change.
- Add/update tests at the appropriate level.
- Run relevant validation; report exact commands and outcomes.
- Update only documentation/registers affected by the change.
- Do not mark a finding/requirement/ADR Verified or Closed without traceable evidence.

## 6. Documentation maintenance
At task close, evaluate and update as applicable:
- `CURRENT-STATE.md`, `ACTIVE-WORK.md`, `AI-HANDOFF.md`, `WORK-LOG.md`
- requirements/acceptance/business-rule traceability
- ADR and decision registers
- risk/security registers
- API contracts, diagrams, data dictionary, runbooks
- evidence and audit finding statuses

Use Markdown/CSV/YAML/Mermaid as editable sources of truth. PDF/DOCX are publication outputs, not competing masters. Avoid duplicating canonical content.

## 7. Required task report
Every substantial task report must state:
1. Scope and explicit non-scope.
2. Branch and before/after commit SHA (if available).
3. Files changed and why.
4. Decisions made vs decisions merely proposed.
5. Tests/commands run and exact results.
6. Tests not run and why.
7. Known gaps, risks, and deviations.
8. Documentation/registers updated.
9. Next actionable task.
10. Whether the work is ready for human review; never claim merge/deploy unless confirmed.

## 8. Stop conditions
Stop and ask the owner when:
- an unresolved requirement/ADR conflict changes the implementation;
- a security or tenant-isolation boundary is unclear;
- a destructive migration/data change lacks an approved recovery plan;
- the task requires a product/business policy not specified in approved sources;
- the requested scope conflicts with a higher-priority accepted decision;
- credentials, production access, external spend, or deployment approval are required.
