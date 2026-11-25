# Active Work

> Initial work queue imported from Documentation-to-Code Audit Rev2.0 and updated after Gate 1 closure (PR #3, `e204c9e`).

## Current objective
Close remaining Engineering Baseline gates (Gate 2–4) before expanding into the next major commerce feature (Merchant Write APIs / Identity Integration / Cart & Checkout).

## Gate 1 — Requirements and business-rule traceability (`READY FOR INDEPENDENT REVIEW` — Pending PO Sign-off on `CR-G1-001`, `CR-G1-004` & `OD-001`–`OD-008`)
- [x] `AUD-001` Reconciled `AC-07` (preserving Phase 02 local/staging/API/tests meaning) and mapped Rev2.0 `AC-07`–`AC-10` in `Acceptance-Criteria.csv` + `Acceptance-Criteria-Reconciliation.md` (pending PO sign-off).
- [x] `AUD-002` Populated `FR-001`–`FR-042` traceability with separated `ImplementationStatus` / `VerificationStatus` and `NFR-001`–`NFR-011` crosswalk (ready for independent review).
- [x] `AUD-003` Mapped `BR-001`–`BR-012` to invariants, aggregates, SQL constraints, and tests without conflating `BR-003`/`ST-02` or `BR-009`/`AC-05`.
- [x] `AUD-007` Restored complete Use Case specifications (`UC-01`–`UC-10`) with Gherkin examples and explicit open decisions in `docs/01-Requirements/Use-Case-Specifications.md`.

## Gate 2 — Commerce correctness contracts (Next)
- [ ] `AUD-004` Separate `OrderStatus`, `FulfillmentStatus`, and `PaymentStatus` state models; define cross-state policies and tests.
- [ ] `AUD-005` Redesign Checkout idempotency with atomic key claim, unique constraint, concurrent duplicate handling, and replay behavior. Do not implement Checkout before owner-approved contract.

## Gate 3 — Baseline completeness and quality
- [ ] `AUD-006` Produce separate Current and Target ERDs plus column-level Data Dictionary.
- [ ] `AUD-008` Correct C4 context/container diagrams and label Current vs Target accurately.
- [ ] `AUD-009` Map security controls to applicable OWASP ASVS requirements and concrete tests/evidence.
- [ ] `AUD-010` Obtain owner-approved NFR targets (latency, scale, availability, RPO/RTO, retention, cost); do not invent values.

## Gate 4 — Governance and consistency
- [ ] `AUD-011` Reconcile roadmap status with verified Product Host/catalog implementation in Rev2.0 `phase-roadmap.csv`.
- [ ] `AUD-012` Refresh CI evidence from actual workflow run and exact SHA in Rev2.0 `baseline-evidence.md`.
- [ ] `AUD-013` Resolve duplicate source-of-truth drift between handbook Chapter 21 and standalone chapter.
- [ ] `AUD-014` Validate OpenAPI version/tooling compatibility and ensure error responses are represented; confirm against current official spec before changing version.
- [ ] `AUD-015` Remove hard-coded CI SQL credential and adopt ephemeral/secret-based handling; rotate if reused.

## Next recommended task
1. Obtain Product Owner confirmation on `CR-G1-001` and `OD-001`–`OD-005` in `docs/08-Engineering-Management/Change-Log-and-Open-Decisions.md`.
2. Execute **Gate 2** (`AUD-004` Order/Fulfillment/Payment state models and `AUD-005` Checkout idempotency contract) on `feature/public-store-catalog-read`.

## Exit rule
Do not begin a major new commerce feature solely because the roadmap lists it. First close blocking requirements/decision gates or obtain an explicit owner-approved exception.
