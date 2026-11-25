# Engineering Change Log & Open Decisions (Gate 1)

**Document ID:** `RF-G1-CL-OD` | **Gate:** `Gate 1 (Requirements & Business Baseline)`
**Baseline Snapshot:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d` (PR #1 / `b73d30cc0656b5c4f2e3a867bda7885135d047e8` docs baseline)

This register records every material requirement/acceptance change request (`CR-*`) and unresolved product/engineering decision (`OD-*`) identified during Gate 1. In accordance with Gate 1 governance, no Product Owner decision is fabricated or self-approved.

---

## 1. Change Requests (`CR-*`)

### CR-G1-001 — Reconcile `AC-07` Identifier Conflict & Deprecate Duplicate Alias
- **Status:** `Proposed (Pending Product Owner Confirmation)`
- **Source / Problem (`AUD-001`):** Phase 02 §8 defines `AC-07` as *Order cancellation releases reserved inventory once*, whereas the reconstructed `Comprehensive-Audit-Report.md` / `Findings.csv` referenced `AC-07` as *FulfillmentAddress hidden from public catalog responses* and `AC-08` as *Store Pickup hidden/disallowed for OnlineOnly stores*.
- **Current Requirement:** Two conflicting definitions of `AC-07` in project documentation.
- **Proposed Change:**
  1. Preserve `AC-07` for its original Phase 02 §8 meaning (*Order cancellation releases reserved inventory once* — `UC-10`, `BR-012`, `OR-09`).
  2. Assign `AC-08` as the single canonical Acceptance Criterion for *Storefront hides `FulfillmentAddress` and blocks/hides `Pickup` for `OnlineOnly` stores* (`ST-03`, `ST-04`, `UC-03`, `UC-06`).
  3. Record `AC-07 (audit alias)` as `DeprecatedAlias` in [Acceptance-Criteria-Reconciliation.md](../01-Requirements/Acceptance-Criteria-Reconciliation.md).
- **Rationale / Evidence:** Preserves original Phase 02 primary requirements numbering without breaking `BR-012` / `UC-10` traceability while keeping `AC-08` implemented and verified by `StoreSettings_PickupAllowedOnlyForPhysicalStores` and `PublicStoreCatalogIntegrationTests`.
- **Options Considered:** (A) Renumber Phase 02 `AC-07` to `AC-09` (rejected: breaks primary Phase 02 baseline); (B) Keep `AC-07` as Phase 02 cancellation criterion and consolidate public-storefront `FulfillmentAddress`/`Pickup` under `AC-08` (selected).
- **Impact Analysis:** Updates [Acceptance-Criteria.csv](../01-Requirements/Acceptance-Criteria.csv), [Acceptance-Criteria-Reconciliation.md](../01-Requirements/Acceptance-Criteria-Reconciliation.md), and cross-references in `Requirements-Traceability-Matrix.csv`. Zero code impact.
- **Related IDs:** `AC-07`, `AC-08`, `BR-012`, `ST-03`, `ST-04`, `UC-03`, `UC-06`, `UC-10`
- **Owner / Approver:** Product Owner & Technical Documentation Architect
- **Date:** `2026-11-26`
- **Verification Plan:** Verified via `scripts/validate-gate1-baseline.ps1` checking AC uniqueness and zero active meaning collisions.

---

### CR-G1-002 — Clarify `BR-004` Active Category Visibility Alongside `PR-02`
- **Status:** `Approved (Engineering Alignment — Matches Verified Implementation)`
- **Source / Problem (`AUD-003`):** Phase 02 `BR-004` states *"Only `Published` products appear to customers"*, while Phase 04 `PR-02` and `PublicCatalogQueryService.cs` additionally require the product's `Category.IsActive == true`.
- **Current Requirement:** `BR-004` mentions only `Product.Status == Published`.
- **Proposed Change:** Clarify `BR-004` scope in [Business-Rules-Traceability-Matrix.csv](../01-Requirements/Business-Rules-Traceability-Matrix.csv) to align with `PR-02`: a product is publicly visible if and only if `Store.Status == Active` (`BR-003`/`ST-02`), `Category.IsActive == true` (`PR-02`), and `Product.Status == Published` (`BR-004`/`PR-02`).
- **Rationale / Evidence:** Verified in `PublicCatalogQueryService.cs` and tested by `Products_ExcludeDraftArchivedInactiveCategoryAndOtherStoreProducts_AndReactivateCleanly`.
- **Options Considered:** (A) Expose published products in inactive categories (rejected: breaks category deactivation); (B) Align `BR-004` with `PR-02` (selected).
- **Impact Analysis:** Documentation traceability clarification only; matches existing code and tests at `04392c875c275e99f4b5ee378f63b3a8b8fc541d`.
- **Related IDs:** `BR-004`, `PR-02`, `FR-012`, `FR-019`, `AC-03`, `UC-06`
- **Owner / Approver:** Principal Software Architect / Product Owner
- **Date:** `2026-11-26`
- **Verification Plan:** Verified by `PublicStoreCatalogIntegrationTests.Products_ExcludeDraftArchivedInactiveCategoryAndOtherStoreProducts_AndReactivateCleanly`.

---

### CR-G1-003 — Bind `BR-006` Price Integrity to Store Currency Minor-Unit Precision (`MNY-01`)
- **Status:** `Approved (Engineering Alignment — Matches Verified Implementation)`
- **Source / Problem (`AUD-003`):** Phase 02 `BR-006` states server-authoritative pricing at checkout; Phase 05 `MNY-01` enforces explicit minor-unit validation (`MoneyPrecision.EnsureValidAmount`) without implicit rounding and binds `Product.CurrencyCode` to `StoreSettings.CurrencyCode`.
- **Current Requirement:** `BR-006` did not explicitly reference `MNY-01` minor-unit precision.
- **Proposed Change:** Link `BR-006` to `MNY-01` and `OR-02` in [Business-Rules-Traceability-Matrix.csv](../01-Requirements/Business-Rules-Traceability-Matrix.csv), recording that product-level currency precision (`MNY-01`) is already `Implemented` while checkout/order-level pricing (`OR-02`) remains `Not Implemented`.
- **Rationale / Evidence:** Verified in `MoneyPrecision.cs`, `Product.Create`, and `ProductCreation_EnforcesStoreSettingsCurrencyPrecisionAndTenantMatch`.
- **Options Considered:** (A) Implicit rounding (rejected: financial accuracy risk); (B) Explicit `MNY-01` validation (selected).
- **Impact Analysis:** Accurate split-status tracking in Gate 1 matrices; zero code change.
- **Related IDs:** `BR-006`, `MNY-01`, `OR-02`, `FR-010`, `FR-027`, `AC-05`, `UC-04`, `UC-08`
- **Owner / Approver:** Database & Backend Architect
- **Date:** `2026-11-26`
- **Verification Plan:** Verified by `ProductCreation_EnforcesStoreSettingsCurrencyPrecisionAndTenantMatch`; checkout `OR-02` queued for Gate 4.

---

### CR-G1-004 — Reconcile Rev2.0 `registers/acceptance-criteria.csv` (`AC-07`–`AC-10`)
- **Status:** `Open (Pending PO Confirmation)`
- **Source / Problem (`AUD-001`):** Rev2.0 [acceptance-criteria.csv](../engineering/baseline/RetailFlow_Engineering_Documentation_AtoZ_Rev2.0/registers/acceptance-criteria.csv) (imported in `2c020de`) defines `AC-07` as *Operational recovery*, `AC-08` as *Session security*, `AC-09` as *API error contract*, and `AC-10` as *Release traceability*, whereas Phase 02 defines `AC-01`–`AC-07` differently.
- **Current Requirement:** Canonical [Acceptance-Criteria.csv](../01-Requirements/Acceptance-Criteria.csv) holds `AC-01`–`AC-08` (`AC-08` = Operational recovery / backup & Data Protection key recovery).
- **Proposed Change:** Map Rev2.0 `AC-07` -> `AC-08` (done in [Acceptance-Criteria-Reconciliation.md](../01-Requirements/Acceptance-Criteria-Reconciliation.md)), and request Product Owner approval to promote Rev2.0 `AC-08` (*Session security*), `AC-09` (*API error contract*), and `AC-10` (*Release traceability*) into `Acceptance-Criteria.csv` as `AC-09`–`AC-11`.
- **Rationale / Evidence:** Preserves Phase 02 IDs without collision while capturing all Rev2.0 operational/security acceptance criteria now present in the repository.
- **Options Considered:** (A) Overwrite Phase 02 IDs (rejected: breaks `BR-012`/`UC-10`); (B) Append non-colliding IDs `AC-09`–`AC-11` after PO approval (selected).
- **Impact Analysis:** Documentation-only register reconciliation; zero code impact.
- **Related IDs:** `AC-07`, `AC-08`, `AUD-001`, `OD-007`
- **Owner / Approver:** Product Owner / Technical Documentation Architect
- **Date:** `2026-11-26`
- **Verification Plan:** Verified in [Acceptance-Criteria-Reconciliation.md](../01-Requirements/Acceptance-Criteria-Reconciliation.md) and `scripts/validate-gate1-baseline.ps1`.

---

## 2. Open Decisions (`OD-*`)

| ID | Topic | Question / Decision Needed | Related IDs | Owner | Target Gate | Status |
|---|---|---|---|---|---|---|
| **OD-001** | Inventory Reservation Expiry Policy | Does an unconfirmed/pending order reservation expire automatically? If so, after how many minutes, and what background/on-demand job releases it? | `FR-030`, `FR-031`, `IN-04`, `OR-01`, `UC-08`, `UC-10` | Product Owner | Gate 2 / Gate 4 | `Pending Product Owner Approval` |
| **OD-002** | Customer Self-Cancellation Cutoff Stage | What is the latest `OrderStatus` / `FulfillmentStatus` at which a Customer can self-cancel (`Pending` only, `Confirmed`, or before `OutForDelivery`)? | `FR-036`, `BR-011`, `OR-08`, `UC-10` | Product Owner | Gate 2 / Gate 4 | `Pending Product Owner Approval` |
| **OD-003** | Merchant Cancellation & Rejection Reasons | Which structured rejection/cancellation reason codes are mandatory for Merchant Owners when rejecting or cancelling orders? | `FR-033`, `FR-036`, `UC-09`, `UC-10` | Product Owner | Gate 2 / Gate 4 | `Pending Product Owner Approval` |
| **OD-004** | Delivery GeoAreas & Fee Schedule (Jordan) | What is the canonical list of Jordan governorates/areas (`DeliveryZone`) and fee/minimum-order rules supported at MVP launch? | `FR-039`, `FR-040`, `FR-041`, `FR-042`, `UC-03`, `UC-08` | Product Owner | Gate 2 / Gate 4 | `Pending Product Owner Approval` |
| **OD-005** | Proof of Delivery / Pickup Evidence | What minimum confirmation data (timestamp, recipient name, optional note/OTP) must be recorded when marking an order `Delivered` or `PickedUp`? | `FR-034`, `FR-035`, `OR-05`, `UC-09` | Product Owner | Gate 2 / Gate 4 | `Pending Product Owner Approval` |
| **OD-006** | Production Session Transport (`AUTH-ADR-01`) | Approve or revise `AUTH-ADR-01` (currently `Proposed`, v0.14) before integrating `UC-01`, `UC-02`, `UC-05`, and protected write APIs. | `FR-016`–`FR-018`, `NFR-001`, `ID-01`–`ID-03`, `UC-01`, `UC-02`, `UC-05` | Security & Architecture Owners | Gate 3 | `Pending Engineering/Security Approval` |
| **OD-007** | Promote Rev2.0 `AC-08..AC-10` into Canonical Register | Confirm whether to promote Rev2.0 `AC-08` (Session security), `AC-09` (API error contract), and `AC-10` (Release traceability) from `docs/engineering/baseline/.../registers/acceptance-criteria.csv` into `Acceptance-Criteria.csv` as `AC-09`–`AC-11`. | `AC-07`, `AC-08`, `AUD-001`, `CR-G1-004` | Product Owner / Doc Owner | Gate 1 / Gate 2 | `Pending Product Owner Confirmation` |
| **OD-008** | Automated Order/Cart Concurrency & Idempotency Standard | Confirm standard concurrency mechanism (`RowVersion` vs pessimistic lock) and `Idempotency-Key` storage/TTL for `UC-08` and `UC-10`. | `FR-030`, `FR-031`, `FR-036`, `NFR-004`, `TX-01`, `INV-01`, `OR-09` | Principal Software Architect | Gate 2 / Gate 4 | `Pending Architecture Approval` |
