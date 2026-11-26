---
trigger: always_on
description: Core RetailFlow operating invariants that apply to every task.
---
# RetailFlow operating invariants
- Read root `AGENTS.md`; it is the canonical contract.
- Read `doc/README.md` and `doc/10-open-items/Production-Gaps-and-Decision-Backlog.md` before substantial work.
- Never equate target documentation, a PoC, or a proposed ADR with Product Host implementation.
- Never invent approvals, evidence, tests, requirements, owners, or performance targets.
- Do not change Accepted decisions silently; propose an ADR and wait for owner approval.
- Preserve tenant isolation, authorization, data integrity, security, and architecture.
- Keep scope narrow; no branch push, PR, merge, deployment, or destructive action without explicit authorization.
- At task close, update affected documentation, traceability (`doc/09-traceability/Feature-Code-Test-Matrix.md`), and open items (`doc/10-open-items/Production-Gaps-and-Decision-Backlog.md`).
