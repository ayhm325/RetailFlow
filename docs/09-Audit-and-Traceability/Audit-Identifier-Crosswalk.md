# RetailFlow — Audit Identifier Crosswalk (`RF-REV-010`)

**Last Verified:** `2026-11-26` | **Branch:** `feature/public-store-catalog-read`

This crosswalk preserves all historical audit and review identifiers across the repository without silently renumbering or deleting prior records.

| Original ID | Source Register | Title / Scope | Canonical / Related ID | Relationship | Current Status | Resolution Evidence / Remaining Work |
|---|---|---|---|---|---|---|
| `AUD-01` | `docs/09-Audit-and-Traceability/Findings.csv` | Outdated bootstrap statements in `product-host-bootstrap.md` | — | Independent (Docs Reconstruction) | **Resolved** | Fixed in PR #2 (`b73d30c`) |
| `AUD-02` | `Findings.csv` | Search case-insensitivity depends on SQL CI collation | — | Independent (Database) | **Open (Informational)** | Documented in `Database-Design-and-Migrations.md` |
| `AUD-03` | `Findings.csv` | `MultipleActiveResultSets=True` disables EF Core savepoints | — | Independent (Database) | **Open (Informational)** | Noted in `Database-Design-and-Migrations.md` & `ADR-0006` |
| `AUD-04` | `Findings.csv` | Tracked `RetailFlow-Catalog-Hardening.patch` in root | — | Independent (Hygiene) | **Resolved** | Removed in `d867bc2` |
| `AUD-05` | `Findings.csv` | Partial ISO 4217 currency seed list (8 currencies) | — | Independent (Domain Data) | **Open (Informational)** | Full SIX list pending global onboarding |
| `AUD-06` *(Findings.csv)* | `Findings.csv` | No dedicated Domain/Application unit test project | — | Distinct from `AUD-006` in `ACTIVE-WORK.md` | **Resolved** | `tests/RetailFlow.Domain.UnitTests` added (`9/9` passing) |
| `AUD-001`–`AUD-003`, `AUD-007` | `ACTIVE-WORK.md` / Rev2.0 Audit | Gate 1 Requirements, BR, UC, AC reconciliation | — | Gate 1 | **Ready for Review** | Validated via `scripts/validate-gate1-baseline.ps1` |
| `AUD-004` | `ACTIVE-WORK.md` / Rev2.0 Audit | Separate `OrderStatus`/`FulfillmentStatus`/`PaymentStatus` | `ADR-0005`, `RF-REV-004` | Gate 2 | **Implemented (`37f80f5`) / ADR Enhanced** | `Order.cs`, `OrderDomainTests.cs`, `ADR-0005` |
| `AUD-005` | `ACTIVE-WORK.md` / Rev2.0 Audit | Atomic Checkout idempotency contract | `ADR-0006`, `RF-REV-003` | Gate 2 | **Implemented (`37f80f5`) / ADR Enhanced** | `CheckoutService.cs`, `ADR-0006` (stale-lease recovery worker pending) |
| `AUD-006` *(ACTIVE-WORK.md)* | `ACTIVE-WORK.md` / Rev2.0 Audit | Current vs Target ERDs + Data Dictionary | — | Gate 3 (Distinct from `AUD-06` above) | **Partially Resolved** | As-Is `Data-Dictionary.md` created; Target ERD pending |
| `AUD-008` | `ACTIVE-WORK.md` / Rev2.0 Audit | C4 context/container diagrams | — | Gate 3 | **Open** | Pending Gate 3 diagram update |
| `AUD-009` | `ACTIVE-WORK.md` / Rev2.0 Audit | OWASP ASVS security controls mapping | `RF-REV-002`, `RF-REV-008` | Gate 3 | **Resolved (Scoped)** | `docs/07-Security/OWASP-ASVS-Security-Mapping.md` (ASVS 5.0.0) |
| `AUD-010`–`AUD-013` | `ACTIVE-WORK.md` / Rev2.0 Audit | NFR targets, roadmap/CI/handbook sync | `RF-REV-009` | Gate 3 / Gate 4 | **Open / In Progress** | `AUD-010` numeric SLOs pending Owner |
| `AUD-014` | `ACTIVE-WORK.md` / Rev2.0 Audit | OpenAPI error response representation | `RF-REV-008` | Gate 4 | **Partially Resolved** | Development `/openapi/v1.json` verified; API9 lifecycle open |
| `AUD-015` | `ACTIVE-WORK.md` / Rev2.0 Audit | Hard-coded CI SQL credential | `RF-REV-001` | Gate 4 | **Fixed Locally** | `.github/workflows/ci.yml` single-job Docker SQL + masked `$GITHUB_ENV` |
| `RF-REV-001`–`RF-REV-010` | Corrective Engineering Mission (`2026-11-26`) | 10-workstream corrective review & fixes | Linked above | Corrective Review | **Verified Locally** | Documented in `WORK-LOG.md` & final report |
