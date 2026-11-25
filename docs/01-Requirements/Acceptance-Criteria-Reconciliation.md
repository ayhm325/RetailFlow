# Acceptance Criteria Reconciliation (`AUD-001`)

**Last Verified:** `2026-11-26` | **Docs Baseline:** PR #3 (`e204c9e`) | **Code Snapshot:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Canonical Source of Truth
The primary product acceptance criteria originate from **Phase 02 PDF (`v1.0`, p. 9, `AC-01`–`AC-07`)**, and the consolidated Gate 1 working register is [Acceptance-Criteria.csv](Acceptance-Criteria.csv) (`AC-01`–`AC-08`). All chapter summaries and traceability matrices must reference IDs from that register rather than redefining criteria inline.

## 2. Why Conflict Occurred (`AC-07` and Rev2.0 `AC-08`–`AC-10`)
1. **Original Phase 02 `AC-07` (`v1.0`, p. 9):** *"يمكن تشغيل التطبيق محلياً ونشر نسخة تجريبية مع API موثق واختبارات أساسية"* — Local run, Preview/Staging deployment, documented API, and baseline tests. It does **not** mean releasing a reservation once (reservation release is specified in `FR-037`, `BR-009`, `UC-10`, `IN-04`, and `OR-08`).
2. **Rev2.0 Register ([acceptance-criteria.csv](../engineering/baseline/RetailFlow_Engineering_Documentation_AtoZ_Rev2.0/registers/acceptance-criteria.csv)):** Re-numbered acceptance criteria into `AC-01`–`AC-10`, placing *Operational recovery (backup restore & Data Protection key recovery)* under `AC-07`, and adding `AC-08` (*Session security*), `AC-09` (*API error contract*), and `AC-10` (*Release traceability*).

## 3. Complete Source Crosswalk

| Original Source & Version | Original ID | Original Meaning | Gate 1 ID | Reconciliation Status | Related FRs / UCs | Evidence / Verification Status | Pending Owner Decision |
|---|---|---|---|---|---|---|---|
| Phase 02 PDF (`v1.0`, p. 9) | `AC-01` | Two trial stores provisioned with distinct URL, identity, and settings | `AC-01` | **Preserved** (`Approved` source) | `FR-001`–`FR-005` / `UC-01`–`UC-03` | `Partially Implemented` / `Partially Verified` (catalog slug read isolation only) | None |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-02` | Merchant cannot read/modify another store's record by changing ID | `AC-02` | **Preserved** (`Approved` source) | `FR-004`,`FR-008`,`FR-009`,`FR-032`–`FR-034` / `UC-03`,`UC-04`,`UC-09`,`UC-10` | `Not Implemented` / `Not Verified` (Merchant auth absent) | None |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-03` | Customer registers and shops inside one store without seeing other stores | `AC-03` | **Preserved** (`Approved` source) | `FR-016`,`FR-017`,`FR-019`–`FR-021` / `UC-05`,`UC-06` | `Partially Implemented` / `Partially Verified` (public catalog verified; auth PoC only) | None |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-04` | Customer completes COD Checkout with server-computed prices and fees | `AC-04` | **Preserved** (`Approved` source) | `FR-023`–`FR-029`,`FR-031`,`FR-039`–`FR-042` / `UC-07`,`UC-08` | `Not Implemented` / `Not Verified` | None |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-05` | Concurrent purchases of last unit never produce negative inventory | `AC-05` | **Preserved** (`Approved` source) | `FR-014`,`FR-030` / `UC-08` | `Partially Implemented` / `Not Verified` (SQL CHECKs exist; concurrent checkout absent) | None |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-06` | Merchant processes order through `Delivered` or `PickedUp` | `AC-06` | **Preserved** (`Approved` source) | `FR-032`–`FR-036` / `UC-09` | `Not Implemented` / `Not Verified` | None |
| Phase 02 PDF (`v1.0`, p. 9) | `AC-07` | Local run, Preview/Staging deployment, documented API & baseline tests | `AC-07` | **Preserved** (`Approved` source) | `FR-019`–`FR-021` / `UC-06` | `Partially Implemented` / `Partially Verified` (Local/CI pass; Staging deploy absent) | None |
| Rev2.0 `acceptance-criteria.csv` | `AC-07` | Operational recovery (backup restore, schema/domain data & Data Protection keys) | `AC-08` | **Re-assigned** (`Proposed`, `CR-G1-001`) | `FR-016` / `UC-05` (`NFR-011` / `NFR-REC-01`) | `PoC Only` / `Not Verified` | `CR-G1-001` (PO/Ops approval) |
| Rev2.0 `acceptance-criteria.csv` | `AC-08` | Session security (`AUTH-ADR-01`, CSRF, revocation, expiry, multi-store tabs) | *(Proposed `AC-09`)* | **Tracked in `OD-007`** (not lost; covered by `NFR-001`, `ID-01`–`ID-03`) | `FR-016`,`FR-017` / `UC-05` | `PoC Only` / `Not Verified` | `CR-G1-004` / `OD-007` |
| Rev2.0 `acceptance-criteria.csv` | `AC-09` | API error contract (`ValidationProblemDetails`/`ProblemDetails` without internals) | *(Proposed `AC-10`)* | **Tracked in `OD-007`** (not lost; verified in `PublicStoreCatalogIntegrationTests`) | `FR-019`–`FR-021` / `UC-06` (`NFR-001`, `NFR-007`) | `Partially Implemented` / `Partially Verified` (`PublicStoreCatalogIntegrationTests`) | `CR-G1-004` / `OD-007` |
| Rev2.0 `acceptance-criteria.csv` | `AC-10` | Release traceability (every in-scope requirement has test evidence or approved deviation) | *(Proposed `AC-11`)* | **Tracked in `OD-007`** (not lost; enforced by Gate 1/4 traceability) | All `FR-001`–`FR-042` | `Not Verified` (Release governance gate) | `CR-G1-004` / `OD-007` |

## 4. Governance Rules
- `AC-07` retains its original Phase 02 meaning exclusively (`Local Run, Staging, API Documentation & Baseline Tests`).
- No ID is silently renumbered or overwritten; Rev2.0's displaced `AC-07` is preserved under `AC-08` (`Proposed`), and Rev2.0 `AC-08`–`AC-10` remain explicitly tracked under `CR-G1-004` / `OD-007` awaiting Product Owner approval.
