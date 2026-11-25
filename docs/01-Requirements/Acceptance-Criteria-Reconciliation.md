# Acceptance Criteria Reconciliation (`AUD-001`)

**Last Verified:** `2026-11-26` | **Baseline Commit:** `b73d30cc0656b5c4f2e3a867bda7885135d047e8` (Historical Ref: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`)

## 1. Canonical Source of Truth
The single canonical source for Acceptance Criteria is [Acceptance-Criteria.csv](Acceptance-Criteria.csv). All chapter summaries and traceability matrices must reference `AC-01`..`AC-08` from that file rather than redefining criteria inline.

## 2. Conflict Investigation (`AC-07` and `AC-08`..`AC-10`)

| Source / Version | Old ID | Reported / Observed Meaning | Authority Assessment | Disposition |
|---|---|---|---|---|
| Phase 02 PDF (`v1.0`, p. 9) | `AC-01`..`AC-06` | Multi-store provisioning, cross-tenant denial, customer scoping, COD checkout, concurrency-safe stock, merchant fulfillment | Primary product requirements source | **Retained** as `AC-01`..`AC-06` |
| Phase 02 PDF (`v1.0`, p. 9) & Handbook Ch. 02 (reported) | `AC-07` | Local run, staging/preview deployment, documented API, and baseline tests | Primary product requirements source (owns original `AC-07`) | **Retained** as `AC-07` |
| `registers/acceptance-criteria.csv` (reported in Gate 1 brief; **file absent from local workspace**) | `AC-07` (conflicting) | Backup restore, schema/domain data recovery, and Data Protection key recovery | Secondary operational register; reused `AC-07` ID with a different meaning | **Deprecated as `AC-07` alias**; preserved as **`AC-08`** (`Proposed`, pending Ops/Security sign-off) |
| `registers/acceptance-criteria.csv` (reported in Gate 1 brief; **file absent from local workspace**) | `AC-08`..`AC-10` | Exact definitions unavailable because `registers/acceptance-criteria.csv` is not present in the local workspace | Unverifiable without missing CSV | **Recorded as Missing Input (`OD-001`)**; not fabricated |

## 3. Alias & Deprecation Rule
- `AC-07` retains its original Phase 02 meaning exclusively.
- Operational backup/Data Protection key recovery is tracked under `AC-08` (`Proposed`) so no ID is ever reused with two meanings.
- If the external `registers/acceptance-criteria.csv` containing `AC-08`..`AC-10` is later restored to the repository, its remaining items must be mapped to `AC-09`+ after checking for duplicates against `AC-01`..`AC-08` (`OD-001`).
