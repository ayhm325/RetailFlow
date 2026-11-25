# Acceptance Criteria Reconciliation (`AUD-001`)

**Last Verified:** `2026-11-26` | **Main HEAD:** `e204c9e6f0d570be24fbdb2750889dd464d1e077` (Code Snapshot: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`)

## 1. Canonical Source of Truth
The single canonical source for Product Acceptance Criteria is [Acceptance-Criteria.csv](Acceptance-Criteria.csv) (`AC-01`–`AC-08`). All chapter summaries and traceability matrices must reference IDs from that file rather than redefining criteria inline.

## 2. Conflict Investigation (`AC-07` and Rev2.0 `AC-08`–`AC-10`)

During Gate 1, a conflict was identified between Phase 02 (`v1.0`, p. 9) and the Rev2.0 baseline register ([Rev2.0 acceptance-criteria.csv](../engineering/baseline/RetailFlow_Engineering_Documentation_AtoZ_Rev2.0/registers/acceptance-criteria.csv), imported in `2c020de`). The table below reconciles both sources without losing or overwriting any ID:

| Source / Version | Source ID | Meaning in Source | Canonical Mapping | Disposition |
|---|---|---|---|---|
| Phase 02 PDF (`v1.0`, p. 9) | `AC-01`..`AC-06` | Multi-store provisioning, cross-tenant denial, customer scoping, COD checkout, concurrency-safe stock, merchant fulfillment | `AC-01`..`AC-06` | **Retained** in `Acceptance-Criteria.csv` |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-07` | Order cancellation releases reserved inventory once (and baseline run/test readiness) | `AC-07` | **Retained** as canonical `AC-07` |
| Rev2.0 `acceptance-criteria.csv` | `AC-07` | Operational recovery (backup restore, schema/domain data & Data Protection key recovery) | `AC-08` | **Deprecated as `AC-07` alias**; tracked as **`AC-08`** (`Proposed`, pending Ops/Security sign-off) |
| Rev2.0 `acceptance-criteria.csv` | `AC-08` | Session security (final ADR compliance, CSRF, revocation, expiry, multi-store browser semantics) | `OD-007` (Gate 3 / `AUTH-ADR-01`) | **Pending PO/Security promotion** to `AC-09` (covered by `NFR-001`, `ID-01`–`ID-03`) |
| Rev2.0 `acceptance-criteria.csv` | `AC-09` | API error contract (`ValidationProblemDetails` / `ProblemDetails` without leaking internals) | `OD-007` (Verified in `PublicStoreCatalogIntegrationTests`) | **Pending PO promotion** to `AC-10` (already verified in `FR-022` / `NFR-007`) |
| Rev2.0 `acceptance-criteria.csv` | `AC-10` | Release traceability (every in-scope requirement has test evidence or approved deviation) | `OD-007` (Release Gate) | **Pending PO promotion** to `AC-11` (enforced by Gate 1/4 traceability) |

## 3. Alias & Deprecation Rule
- `AC-07` retains its original Phase 02 meaning exclusively in `Acceptance-Criteria.csv`.
- Operational backup/Data Protection key recovery is tracked under `AC-08` (`Proposed`) so no ID is ever reused with two meanings.
- Promotion of Rev2.0 `AC-08`–`AC-10` into `Acceptance-Criteria.csv` as `AC-09`–`AC-11` awaits Product Owner confirmation under `CR-G1-004` / `OD-007`.
