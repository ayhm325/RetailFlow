# Active Work

> Initial work queue imported from Documentation-to-Code Audit Rev2.0. Reconfirm against current HEAD before execution.

## Current objective
Establish a consistent, evidence-backed Engineering Baseline before expanding into the next major commerce feature.

## Gate 1 — Requirements and business-rule traceability (highest priority)
- [ ] `AUD-001` Resolve duplicate/conflicting AC-07 definitions and make one canonical Acceptance Criteria source.
- [ ] `AUD-002` Replace generic acceptance evidence for all 42 FR rows with explicit AC/BR/NFR/Test/source/commit/CI/evidence links.
- [ ] `AUD-003` Map original `BR-001..BR-012` one-to-one to current invariant IDs, aggregates, database constraints, and tests.

## Gate 2 — Commerce correctness contracts
- [ ] `AUD-004` Separate OrderStatus, FulfillmentStatus, and PaymentStatus state models; define cross-state policies and tests.
- [ ] `AUD-005` Redesign Checkout idempotency with atomic key claim, unique constraint, concurrent duplicate handling, and replay behavior. Do not implement Checkout before owner-approved contract.

## Gate 3 — Baseline completeness and quality
- [ ] `AUD-006` Produce separate Current and Target ERDs plus column-level Data Dictionary.
- [ ] `AUD-007` Restore detailed Use Case specifications and links to rules/tests.
- [ ] `AUD-008` Correct C4 context/container diagrams and label Current vs Target accurately.
- [ ] `AUD-009` Map security controls to applicable OWASP ASVS requirements and concrete tests/evidence.
- [ ] `AUD-010` Obtain owner-approved NFR targets (latency, scale, availability, RPO/RTO, retention, cost); do not invent values.

## Gate 4 — Governance and consistency
- [ ] `AUD-011` Reconcile roadmap status with verified Product Host/catalog implementation.
- [ ] `AUD-012` Refresh CI evidence from actual workflow run and exact SHA.
- [ ] `AUD-013` Resolve duplicate source-of-truth drift between handbook Chapter 21 and standalone chapter.
- [ ] `AUD-014` Validate OpenAPI version/tooling compatibility and ensure error responses are represented; confirm against current official spec before changing version.
- [ ] `AUD-015` Remove hard-coded CI SQL credential and adopt ephemeral/secret-based handling; rotate if reused.

## Next recommended task
Start with `AUD-001`: inspect both AC-07 definitions and all AC rows in the handbook chapter and CSV. Propose a canonical definition and mapping; do not choose or rewrite business meaning without owner confirmation.

## Exit rule
Do not begin a major new commerce feature solely because the roadmap lists it. First close blocking requirements/decision gates or obtain an explicit owner-approved exception.
