# Use Case Specifications (UC-01 – UC-10)

**Document ID:** `RF-G1-UC-SPEC` | **Gate:** `Gate 1 (Requirements & Business Baseline)`
**Baseline Snapshot:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d` (PR #1 / `b73d30cc0656b5c4f2e3a867bda7885135d047e8` docs baseline)
**Primary Source:** Phase 03 — *Use Case Catalogue* (`03_RetailFlow_Use_Case_Catalogue_AR.pdf`, v1.0, 2026-11-22)
**Canonical Registers:** [Requirements-Traceability-Matrix.csv](../09-Audit-and-Traceability/Requirements-Traceability-Matrix.csv), [Business-Rules-Traceability-Matrix.csv](Business-Rules-Traceability-Matrix.csv), [Acceptance-Criteria.csv](Acceptance-Criteria.csv), [NFR-Register.csv](NFR-Register.csv)

---

## 1. Cross-Cutting Use Case Rules (Phase 03 §5)

| Rule ID | Statement | Enforcement Layer |
|---|---|---|
| **UC-C01** | Every administrative or merchant write operation verifies identity and role/permission on the server. | Server / Authorization (`Pending Auth Gate`) |
| **UC-C02** | Every read or write of store-scoped data is bound to the target `StoreId` scope. | Application & Persistence (`REL-01`) |
| **UC-C03** | The server is the authoritative source for product prices, delivery fees, and order totals. | Application / Domain (`BR-006`, `OR-02`) |
| **UC-C04** | Every product, cart, order, and inventory record belongs to exactly one store (`StoreId`). | Domain & SQL Composite FKs (`REL-01`) |
| **UC-C05** | Order creation/cancellation and inventory reservation/release must be atomic and roll back cleanly on failure. | Application / Persistence (`TX-01`, `INV-01`) |
| **UC-C06** | Every state transition obeys domain state-machine rules rather than client-supplied status values. | Domain (`ST-02`, `OR-04`, `OR-05`) |
| **UC-C07** | Sensitive state transitions are audited with timestamp and acting principal. | Application / Audit (`NFR-008`) |

---

## 2. Detailed Use Case Specifications

### UC-01 — Submit Store Application

#### 1. Identity & Metadata
- **ID:** `UC-01`
- **Stable Name:** Submit Store Application (`تقديم طلب إنشاء متجر`)
- **Short Goal:** Allow a prospective Merchant Owner to submit a formal application to establish an independent store on RetailFlow.
- **Scope / Boundary:** RetailFlow Platform Onboarding (Public/Merchant Application Boundary).
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Not Implemented` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Merchant Onboarding Module
- **Source:** Phase 03 §4 (`UC-01`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Merchant Owner` (prospective merchant applicant).
- **Supporting Actors:** `System` (validation & persistence), `Platform Admin` (downstream reviewer in `UC-02`).
- **Actor Authority & Trust Boundary:** Unauthenticated or newly registering applicant outside any store tenant boundary. Cannot activate the store, bypass approval, or access other stores. Server must normalize and validate `StoreSlug` and reject duplicate active applications.

#### 3. Stakeholder Interests & Success Guarantee
- **Merchant Owner:** Wants a clear, fast store application submission with immediate confirmation.
- **Platform Admin:** Wants clean, validated, unique store applications queued in `PendingApproval`.
- **Success Guarantee:** A store record is persisted in `PendingApproval` status with normalized unique `StoreSlug`, chosen `StoreType`, and confirmation reference.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Prospective Merchant Owner opens the store creation/application page.
- **Preconditions:**
  - No active/pending duplicate store application exists for the same account/slug conflict.
  - Registration and contact details submitted are verifiable/valid.

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** On validation failure or slug collision, no partial store record is created; existing stores remain untouched.
- **Success Postconditions:** Store exists with `StoreStatus = PendingApproval`; store is **not** visible in public catalog (`UC-06`) and cannot receive orders (`UC-08`).

#### 6. Main Success Scenario
1. Merchant Owner opens the store application page.
2. Merchant Owner enters account details and contact information.
3. Merchant Owner enters proposed store name and `StoreSlug`.
4. Merchant Owner selects store type: `Physical` (`Physical Store`) or `OnlineOnly` (`Online Only`).
5. Merchant Owner submits the application.
6. System validates input, normalizes `StoreSlug`, creates the store in `PendingApproval` status, and returns the application reference / confirmation.

#### 7. Alternate & Exception/Error Flows
- **7a. Duplicate or Reserved `StoreSlug` (at Step 6):**
  - *Outcome:* System rejects the submission with validation error and prompts for an alternative `StoreSlug`.
- **7b. Missing or Invalid Mandatory Fields (at Step 6):**
  - *Outcome:* System returns field-level validation errors; no store record is created.
- **7c. Attempt to Receive Orders Before Activation:**
  - *Outcome:* Blocked by `ST-02` / `BR-003`; store remains inaccessible in public catalog and checkout until `UC-02` approves it.

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-001`, `FR-002`, `FR-003`, `FR-004`, `FR-005`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-007`
- **Business Rules & Invariants:** `BR-001`, `BR-003`, `ST-01`, `ST-02`
- **Acceptance Criteria:** `AC-01`
- **Cross-Cutting Rules:** `UC-C01`, `UC-C06`, `UC-C07`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Store entity is platform-owned until approved; applicant owns only their submitted application data.
- `StoreSlug` uniqueness is global across the platform (`UX_Stores_Slug`).

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Concurrency:** Concurrent submissions with the same normalized `StoreSlug` must be resolved safely by unique constraint (`UX_Stores_Slug`) so only one succeeds.
- **Audit Event:** `StoreApplicationSubmitted` (timestamp, proposed slug, store type, applicant identity).

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-01 Submit Store Application
  Scenario: Valid store application enters PendingApproval
    Given no store exists with slug "amman-roastery"
    When a prospective merchant submits an application for "Amman Roastery" with slug "Amman-Roastery" and type "Physical"
    Then the store is created with normalized slug "amman-roastery" and status "PendingApproval"
    And public catalog queries for "amman-roastery" return 404 Not Found

  Scenario: Duplicate StoreSlug is rejected
    Given a store already exists with slug "amman-roastery"
    When another merchant submits an application with slug "amman-roastery"
    Then the application is rejected with a duplicate slug validation error
```
- **Planned Negative/Abuse Tests:** Invalid/empty slug, SQL-unsafe slug characters, concurrent duplicate slug submissions, attempting to submit `Status = Active` directly in payload (server ignores/rejects client-supplied status per `UC-C06`).

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Open Decisions:** `OD-006` (Merchant authentication/identity provisioning mechanism for `UC-01` depends on `AUTH-ADR-01` / Gate 3).
- **Out-of-Scope:** Multi-store ownership per single merchant account (not in MVP).
- **Evidence Status:** `Domain / Schema Partial` (`Store.NormalizeSlug`, `UX_Stores_Slug`, `StoreType`, `StoreStatus.PendingApproval` exist and are tested in `Store` domain/SQL tests); Onboarding HTTP endpoint & workflow `Not Implemented`.

---

### UC-02 — Review Store Application

#### 1. Identity & Metadata
- **ID:** `UC-02`
- **Stable Name:** Review Store Application (`مراجعة طلب المتجر`)
- **Short Goal:** Enable a Platform Admin to approve, reject, or later suspend a store based on platform eligibility rules.
- **Scope / Boundary:** RetailFlow Platform Administration.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Not Implemented` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Platform Administration Module
- **Source:** Phase 03 §4 (`UC-02`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Platform Admin`.
- **Supporting Actors:** `System` (state transition & audit logging).
- **Actor Authority & Trust Boundary:** Privileged platform operator (`PlatformAdmin` role). Cross-tenant visibility is strictly limited to store review/moderation metadata, not arbitrary customer/order mutation (`UC-C01`).

#### 3. Stakeholder Interests & Success Guarantee
- **Platform Admin:** Needs clear queue of `PendingApproval` applications and immutable audit trail of decisions.
- **Merchant Owner:** Needs timely approval to start configuring and selling, or a clear rejection reason.
- **Success Guarantee:** Store transitions to `Active` (on approval) or `Rejected` (with recorded reason), or an `Active` store transitions to `Suspended`, with admin identity and timestamp recorded.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Platform Admin opens the pending store applications queue.
- **Preconditions:**
  - Actor is authenticated as `Platform Admin`.
  - Target store application exists in `PendingApproval` (for approve/reject) or `Active` (for suspend).

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Unauthorized users or invalid duplicate transitions leave store status unchanged.
- **Success Postconditions:** Decision, admin identity, timestamp, and reason (if rejected/suspended) are persisted.

#### 6. Main Success Scenario
1. Platform Admin opens the pending store applications list.
2. Platform Admin selects an application and reviews merchant and store details.
3. Platform Admin chooses `Approve` or `Reject`.
4. On `Approve`, System records the decision and transitions the store to `Active`.
5. On `Reject`, System records the decision and mandatory rejection reason and transitions the store to `Rejected`.

#### 7. Alternate & Exception/Error Flows
- **7a. Application Already Processed (at Step 4–5):**
  - *Outcome:* System rejects invalid repeat transition (`ST-02` / `UC-C06`).
- **7b. Non-Admin Caller (at Step 1–5):**
  - *Outcome:* Operation rejected with authorization failure (`403 Forbidden`).
- **7c. Suspend an Active Store (Post-Approval Alternate Flow):**
  - *Outcome:* Platform Admin suspends an `Active` store with a recorded administrative reason; public catalog immediately stops serving it (`ST-02`).

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-006`, `FR-007`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-008`
- **Business Rules & Invariants:** `BR-003`, `ST-02`
- **Acceptance Criteria:** `AC-01`
- **Cross-Cutting Rules:** `UC-C01`, `UC-C06`, `UC-C07`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Requires `PlatformAdmin` authorization claim verified server-side.
- Merchants and Customers cannot invoke store review transitions.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Concurrency:** Concurrent admin review of the same application must prevent double-approval/contradictory transition.
- **Audit Events:** `StoreApproved`, `StoreRejected`, `StoreSuspended` (with `AdminId`, timestamp, and reason).

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-02 Review Store Application
  Scenario: Platform Admin approves a pending store
    Given store "amman-roastery" is in "PendingApproval"
    When an authenticated Platform Admin approves "amman-roastery"
    Then the store status becomes "Active"
    And the approval timestamp and admin identity are recorded
    And public catalog requests for "amman-roastery" succeed

  Scenario: Merchant cannot self-approve their store
    Given store "amman-roastery" is in "PendingApproval"
    When a Merchant Owner attempts to approve "amman-roastery"
    Then the request is rejected with Forbidden
    And the store status remains "PendingApproval"
```
- **Planned Negative/Abuse Tests:** Merchant self-approval attempt, missing rejection reason, re-approving an already `Rejected` store, suspending an already `Suspended` store.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Dependencies:** `AUTH-ADR-01` / Platform Admin role & auth implementation (Gate 3).
- **Evidence Status:** `Partial (Domain/Read Gate)` — `StoreStatus` enum (`PendingApproval`, `Active`, `Suspended`, `Rejected`) and `StoreDirectoryResolver` public gating are implemented and verified (`NonActiveStores_Return404ProblemDetails`); Admin Review endpoints and state-transition audit log are `Not Implemented`.

---

### UC-03 — Configure Store Identity & Settings

#### 1. Identity & Metadata
- **ID:** `UC-03`
- **Stable Name:** Configure Store Identity & Settings (`إعداد المتجر والهوية`)
- **Short Goal:** Allow an active store's Merchant Owner to customize branding, public/fulfillment addresses, and delivery/pickup options.
- **Scope / Boundary:** Store-Scoped Merchant Configuration.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Partial (Domain/Schema)` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Store Configuration Module
- **Source:** Phase 03 §4 (`UC-03`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Merchant Owner` (authorized for target `StoreId`).
- **Supporting Actors:** `System` (domain invariant enforcement).
- **Actor Authority & Trust Boundary:** Authenticated Merchant Owner scoped strictly to their own `StoreId` (`UC-C02`). Cannot modify another store's settings or enable `Pickup` on an `OnlineOnly` store.

#### 3. Stakeholder Interests & Success Guarantee
- **Merchant Owner:** Wants to configure store name, logo, colors, contact details, delivery zones, and pickup availability.
- **Customer:** Needs accurate public address (only when `Physical`) and never exposed internal `FulfillmentAddress`.
- **Success Guarantee:** Validated `StoreSettings` and branding are saved for the target `StoreId` without affecting any other store.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Merchant Owner opens Store Settings.
- **Preconditions:**
  - Store is approved and `Active`.
  - Merchant Owner is authenticated and authorized for this `StoreId`.

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Invalid settings (e.g. `PickupEnabled = true` on `OnlineOnly` store, or incomplete fulfillment config) are rejected atomically; prior settings remain intact.
- **Success Postconditions:** Store branding and fulfillment configuration are persisted for `StoreId`.

#### 6. Main Success Scenario
1. Merchant Owner opens Store Settings.
2. Merchant Owner updates store name, logo, colors, and contact information.
3. Merchant Owner sets public operating/visit address if `StoreType == Physical`.
4. Merchant Owner enters internal `FulfillmentAddress`.
5. Merchant Owner enables `Delivery` and configures delivery zones/fees when needed.
6. Merchant Owner enables `Store Pickup` if and only if `StoreType == Physical`.
7. Merchant Owner saves changes; System validates invariants and persists settings.

#### 7. Alternate & Exception/Error Flows
- **7a. `OnlineOnly` Store Attempts to Enable `Store Pickup` or Public Visit Address (at Step 3/6/7):**
  - *Outcome:* Rejected by `ST-03` (`StoreSettings.Create` / `UpdateFulfillmentOptions`); `OnlineOnly` stores never expose a public visit address.
- **7b. Incomplete Required Settings for Enabled Service (at Step 7):**
  - *Outcome:* Rejected with validation error; service cannot be enabled without required fields.
- **7c. `FulfillmentAddress` Privacy Rule:**
  - *Outcome:* `FulfillmentAddress` is never returned in public store responses (`UC-06` / `ST-04`).

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-008`, `FR-009`, `FR-038`, `FR-039`, `FR-040`, `FR-041`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-007`
- **Business Rules & Invariants:** `BR-002`, `ST-03`, `ST-04`, `REL-01`
- **Acceptance Criteria:** `AC-01`, `AC-02` (FulfillmentAddress privacy via `FR-006`/`ST-04`; Pickup rule via `FR-041`/`BR-010`/`ST-03`)
- **Cross-Cutting Rules:** `UC-C01`, `UC-C02`, `UC-C04`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- `StoreSettings` and `DeliveryZone` are owned by `StoreId` (`REL-01`).
- Merchant Owner from Store A cannot read or update Store B's settings (`AC-01`).

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Consistency:** Updating settings must validate `Store.StoreType` and `StoreSettings` together consistently.
- **Audit Event:** `StoreSettingsUpdated` (`StoreId`, `MerchantId`, timestamp).

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-03 Configure Store Identity & Settings
  Scenario: OnlineOnly store cannot enable Store Pickup
    Given an Active store with StoreType "OnlineOnly"
    When the Merchant Owner attempts to set PickupEnabled to true
    Then the update is rejected by domain rule ST-03
    And PickupEnabled remains false
```
- **Planned Negative/Abuse Tests:** Cross-tenant settings update attempt, `OnlineOnly` enabling pickup, verifying `FulfillmentAddress` is absent from public `GET /api/stores/{slug}` responses.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Open Decisions:** `OD-004` (Delivery GeoAreas / cities list & fees in Jordan).
- **Evidence Status:** `Partial (Domain/SQL)` — `StoreSettings.Create`, `UpdateFulfillmentOptions`, `CK_StoreSettings_PickupAllowedOnlyForPhysical`, and `StoreSettings_PickupAllowedOnlyForPhysicalStores` test are verified; Merchant Write API & DeliveryZone entities are `Not Implemented`.

---

### UC-04 — Manage Products & Inventory

#### 1. Identity & Metadata
- **ID:** `UC-04`
- **Stable Name:** Manage Products & Inventory (`إدارة المنتجات والمخزون`)
- **Short Goal:** Allow a Merchant Owner to create and manage store-scoped simple products, categories, images, prices, publication states, and stock quantities.
- **Scope / Boundary:** Store-Scoped Catalog & Inventory Management.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Partial (Domain/SQL)` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Catalog & Inventory Modules
- **Source:** Phase 03 §4 (`UC-04`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Merchant Owner` (authorized for target `StoreId`).
- **Supporting Actors:** `System` (domain invariant, currency precision, and tenant FK enforcement).
- **Actor Authority & Trust Boundary:** Authenticated Merchant Owner scoped to their `StoreId`. Cannot reference categories from another store or set negative stock.

#### 3. Stakeholder Interests & Success Guarantee
- **Merchant Owner:** Wants to manage products, SKUs, prices, images, and stock levels cleanly.
- **Customer:** Expects accurate catalog visibility (`Published` + active category) and valid non-negative stock.
- **Success Guarantee:** Product and its `InventoryItem` are saved under `StoreId` with valid currency precision (`MNY-01`), unique non-null SKU per store (`PR-03`), and valid stock invariants (`IN-01`, `IN-02`).

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Merchant Owner opens Product & Inventory Management.
- **Preconditions:**
  - Store is `Active`.
  - Merchant Owner is authenticated and authorized for `StoreId`.
  - Target `Category` belongs to the same `StoreId` (`REL-01`).

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Invalid price precision, duplicate SKU, cross-tenant `CategoryId`, or negative stock leaves existing catalog/inventory unchanged.
- **Success Postconditions:** Product is stored in `Draft`, `Published`, or `Archived` state, linked to same-store `CategoryId` and `InventoryItem`.

#### 6. Main Success Scenario
1. Merchant Owner opens Product Management.
2. Merchant Owner creates a new product or selects an existing product of their store.
3. Merchant Owner enters/updates Name, Description, Images, Price, Category, and optional `SKU`.
4. Merchant Owner sets or adjusts inventory `OnHand` quantity.
5. Merchant Owner selects status: `Draft`, `Published`, or `Archived`.
6. System validates domain rules (`MNY-01`, `PR-01`..`PR-03`, `IN-01`, `IN-02`, `REL-01`) and persists changes.

#### 7. Alternate & Exception/Error Flows
- **7a. Invalid Mandatory Fields or Price Precision (at Step 6):**
  - *Outcome:* Rejected by `MNY-01` (`MoneyPrecision.EnsureValidAmount`) without implicit rounding; product is not published.
- **7b. Duplicate `SKU` in Same Store (at Step 6):**
  - *Outcome:* Rejected by `PR-03` / `UX_Products_StoreId_Sku` (multiple `NULL` SKUs remain allowed).
- **7c. `Draft` or `Archived` Product Visibility:**
  - *Outcome:* Product is saved but excluded from public catalog (`UC-06` / `PR-02`).
- **7d. Cross-Store Product or Category Reference:**
  - *Outcome:* Rejected by domain guard and SQL composite FK `(StoreId, CategoryId)` (`REL-01`).
- **7e. Variant Request (Sizes/Colors):**
  - *Outcome:* Out of scope for MVP (`Simple Products` only per Phase 03 §4).

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-010`, `FR-011`, `FR-012`, `FR-013`, `FR-014`, `FR-015`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-004`, `NFR-006`
- **Business Rules & Invariants:** `BR-002`, `BR-004`, `BR-005`, `BR-006`, `PR-01`, `PR-02`, `PR-03`, `IN-01`, `IN-02`, `MNY-01`, `REL-01`
- **Acceptance Criteria:** `AC-01`, `AC-02`, `AC-03`
- **Cross-Cutting Rules:** `UC-C01`, `UC-C02`, `UC-C04`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- `Category`, `Product`, `ProductImage`, and `InventoryItem` all carry `StoreId` with composite FKs enforcing single-tenant ownership.
- Cross-tenant edits return `403`/`404` and are rejected by persistence constraints.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Concurrency:** Stock adjustments on `InventoryItem` must preserve `0 <= Reserved <= OnHand` (`IN-01`, `IN-02`), e.g. preventing `OnHand` reduction below active `Reserved`.
- **Audit Events:** `ProductPublished`, `ProductArchived`, `InventoryAdjusted`.

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-04 Manage Products & Inventory
  Scenario: Product price must match store currency minor unit
    Given store "amman-roastery" uses currency "JOD" with MinorUnit 3
    When the Merchant Owner creates a product with price 12.3456
    Then creation is rejected by MNY-01 without implicit rounding

  Scenario: Duplicate non-null SKU within the same store is rejected
    Given store "amman-roastery" already has a product with SKU "COF-001"
    When the Merchant Owner creates another product in "amman-roastery" with SKU "COF-001"
    Then creation is rejected by PR-03
```
- **Planned Negative/Abuse Tests:** Cross-tenant `CategoryId` assignment, reducing `OnHand` below `Reserved`, negative `OnHand`, publishing product with `Price < 0` or invalid `MinorUnit`.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Out-of-Scope:** Product variants (size/color matrices) excluded from MVP.
- **Evidence Status:** `Partial (Domain/SQL)` — `Product`, `Category`, `ProductImage`, `InventoryItem`, `MoneyPrecision`, and SQL constraints are implemented and verified (`ProductCreation_EnforcesStoreSettingsCurrencyPrecisionAndTenantMatch`, `DatabaseConstraints_EnforceTenantIsolationAndDomainInvariants`); Merchant Write APIs are `Not Implemented`.

---

### UC-05 — Customer Registration & Sign-In

#### 1. Identity & Metadata
- **ID:** `UC-05`
- **Stable Name:** Customer Registration & Sign-In (`تسجيل العميل والدخول`)
- **Short Goal:** Allow a Customer to register an independent account scoped to a specific active store and sign in to that store only.
- **Scope / Boundary:** Store-Scoped Customer Identity.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `PoC Only` (`IdentitySpike` / `SessionTransportSpike`)
- **Version:** `1.0`
- **Owner:** Product Owner / Identity & Access Module
- **Source:** Phase 03 §4 (`UC-05`), Phase 06

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Customer`.
- **Supporting Actors:** `System` (credential validation, store-scoped account binding, session issuance).
- **Actor Authority & Trust Boundary:** Unauthenticated visitor on an active store link transitions to an authenticated `Customer` **strictly within that single `StoreId`**. Same email in Store B is a completely separate account (`AC-02`).

#### 3. Stakeholder Interests & Success Guarantee
- **Customer:** Wants simple store-specific registration and sign-in without cross-store confusion.
- **Platform / Security:** Wants strict tenant isolation of customer accounts and sessions (`NFR-001`, `NFR-002`).
- **Success Guarantee:** `CustomerAccount` is created/authenticated bound to target `StoreId`, and a store-scoped session is established.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Customer navigates to a store's registration or sign-in page.
- **Preconditions:**
  - Target store exists and is `Active` (`ST-02`).
  - Email and password meet validation rules.

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Failed registration/login does not leak account existence across stores or issue a session.
- **Success Postconditions:** Authenticated customer session bound to `StoreId`; suspended accounts cannot sign in.

#### 6. Main Success Scenario
1. Customer opens the store's registration page.
2. Customer enters Name, Email, and Password.
3. System validates input and verifies the email is not already registered in this `StoreId`.
4. System creates `CustomerAccount` bound to this `StoreId`.
5. Customer signs in via the store's login page.
6. System verifies credentials and confirms the account belongs to the current `StoreId`.

#### 7. Alternate & Exception/Error Flows
- **7a. Email Already Registered in Same Store (at Step 3):**
  - *Outcome:* Registration fails; duplicate account in same store is prevented.
- **7b. Same Email Registered in Another Store (at Step 3):**
  - *Outcome:* Allowed; creates a distinct, independent `CustomerAccount` for the current store (`AC-02`).
- **7c. Suspended Customer Account (at Step 6):**
  - *Outcome:* Sign-in rejected.
- **7d. Cross-Store Access Attempt Using Store A Session (at Step 6):**
  - *Outcome:* Rejected; Store A credentials/session cannot access Store B customer data (`AC-01`, `AC-02`).

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-016`, `FR-017`, `FR-018`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-007`, `NFR-008`
- **Business Rules & Invariants:** `BR-002`, `ST-02`, `ID-01`, `ID-02`, `ID-03`, `REL-01`
- **Acceptance Criteria:** `AC-01`, `AC-02`
- **Cross-Cutting Rules:** `UC-C01`, `UC-C02`, `UC-C04`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Composite unique key `(StoreId, NormalizedEmail)` isolates customer identities per store.
- Session/token claims must bind `StoreId` and be validated against route/request `StoreId`.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Security Constraint:** `AUTH-ADR-01` (Session Transport) remains `Proposed`; Production authentication/cookies/JWT must not be inferred as active until Gate 3 approval.
- **Audit Events:** `CustomerRegistered`, `CustomerSignedIn`, `CustomerSignInFailed`.

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-05 Customer Registration & Sign-In
  Scenario: Same email can register independently in two active stores
    Given "sara@example.com" is registered in Store "store-a"
    When "sara@example.com" registers in Store "store-b"
    Then a separate CustomerAccount is created for "store-b"
    And signing into "store-a" does not grant access to "store-b"
```
- **Planned Negative/Abuse Tests:** Duplicate email in same store, suspended user login, cross-store session replay (`Store A` cookie/token used on `Store B` endpoint).

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Dependencies:** `AUTH-ADR-01` approval and Production Identity integration (Gate 3).
- **Evidence Status:** `PoC Only` — validated in `experiments/IdentitySpike` (`IdentitySpike.Tests`, 14 tests) and `experiments/SessionTransportSpike` (`SessionTransportSpike.Tests`, 45 tests), **not** integrated into `src/` Production Host.

---

### UC-06 — Browse Store & Public Catalog

#### 1. Identity & Metadata
- **ID:** `UC-06`
- **Stable Name:** Browse Store & Public Catalog (`تصفح المتجر والمنتجات`)
- **Short Goal:** Allow a Customer (authenticated or anonymous guest) to browse an active store's identity, active categories, and published products.
- **Scope / Boundary:** Public Storefront Read API (`Vertical Slice 01`).
- **Level:** User Goal
- **Lifecycle Status:** `Approved` & `Implemented`
- **Version:** `1.0`
- **Owner:** Product Owner / Public Catalog Module
- **Source:** Phase 03 §4 (`UC-06`), `PublicCatalogEndpoints.cs`

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Customer` (anonymous or authenticated).
- **Supporting Actors:** `System` (`StoreDirectoryResolver`, `PublicCatalogQueryService`).
- **Actor Authority & Trust Boundary:** Unauthenticated read-only access strictly scoped to an `Active` store resolved by `StoreSlug`. Cannot see `Draft`/`Archived` products, inactive categories, non-active stores, or `FulfillmentAddress`.

#### 3. Stakeholder Interests & Success Guarantee
- **Customer:** Wants fast, deterministic browsing, searching, filtering, sorting, and accurate stock availability.
- **Merchant Owner:** Wants only `Published` products in active categories exposed under their brand.
- **Success Guarantee:** Store identity, active categories, and paginated/filtered `Published` products with computed `AvailableQuantity` are returned exclusively for the target `Active` store.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Customer opens the store link (`/api/stores/{slug}`, `/categories`, `/products`, `/products/{id}`).
- **Preconditions:**
  - Target store exists and has `StoreStatus == Active`.

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Unknown or non-active stores return `404 ProblemDetails` without leaking internal details or stack traces (`NFR-001`, `NFR-007`). Invalid query params return `400 ValidationProblemDetails`.
- **Success Postconditions:** Read-only view rendered; zero state mutation.

#### 6. Main Success Scenario
1. System resolves `StoreSlug` and returns public store identity (hiding `FulfillmentAddress` and respecting `OnlineOnly` public address rules).
2. System returns active ordered categories and published products in active categories.
3. Customer searches by keyword (`q`), filters by category (`categoryId` or `categorySlug`), sorts, and paginates.
4. System applies deterministic filtering, sorting, and pagination (`page`, `pageSize`).
5. Customer opens a specific product's details.
6. System returns description, images, currency-formatted price, and availability (`Available = Max(0, OnHand - Reserved)`).

#### 7. Alternate & Exception/Error Flows
- **7a. Unknown or Non-Active Store (`PendingApproval`, `Suspended`, `Rejected`) (at Step 1):**
  - *Outcome:* Returns `404 ProblemDetails` (`UnknownSlug_Returns404ProblemDetails`, `NonActiveStores_Return404ProblemDetails`).
- **7b. `Draft`, `Archived`, or Inactive-Category Product (at Step 2–6):**
  - *Outcome:* Excluded from list and returns `404` on direct detail lookup (`Products_ExcludeDraftArchivedInactiveCategoryAndOtherStoreProducts_AndReactivateCleanly`).
- **7c. No Matching Results (at Step 4):**
  - *Outcome:* Returns empty deterministic page (`items = []`, `totalCount = 0`).
- **7d. Invalid Query Parameters (e.g. `q > 100`, `page < 1`, invalid sort) (at Step 3–4):**
  - *Outcome:* Returns `400 ValidationProblemDetails` (`InvalidQueryParameters_Return400ValidationProblemDetails`, `SearchOverMaxLength_Returns400ValidationProblemDetails`).
- **7e. Out-of-Stock Product (`Available == 0` or missing `InventoryItem`):**
  - *Outcome:* Displayed with `availableQuantity = 0` / `isInStock = false`; cannot be purchased in `UC-08`.

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-012`, `FR-019`, `FR-020`, `FR-021`, `FR-022`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-003`, `NFR-005`, `NFR-007`, `NFR-009`
- **Business Rules & Invariants:** `BR-001`, `BR-002`, `BR-003`, `BR-004`, `BR-005`, `ST-01`, `ST-02`, `ST-04`, `PR-02`, `IN-03`, `REL-01`
- **Acceptance Criteria:** `AC-01`, `AC-02`, `AC-03`
- **Cross-Cutting Rules:** `UC-C02`, `UC-C04`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Public anonymous read; all queries filter by `StoreId` resolved from `StoreSlug` in `StoreDirectoryResolver`.
- Concurrent requests for different slugs remain strictly isolated (`ConcurrentRequests_ForDifferentStoreSlugs_RemainStrictlyIsolated`).

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- Read-only (`AsNoTracking`), idempotent, safe under concurrent load.

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-06 Browse Store & Public Catalog
  Scenario: Active store returns only published products in active categories
    Given store "fresh-mart" is Active with Published and Draft products
    When a customer requests GET /api/stores/fresh-mart/products
    Then only Published products in active categories of "fresh-mart" are returned
    And Draft, Archived, and other-store products are excluded

  Scenario: Non-active store returns 404 ProblemDetails
    Given store "pending-shop" has status "PendingApproval"
    When a customer requests GET /api/stores/pending-shop
    Then the response status is 404 with ProblemDetails
```
- **Implemented & Verified Tests:** `ActiveStore_ReturnsCategoriesAndProducts`, `UnknownSlug_Returns404ProblemDetails`, `NonActiveStores_Return404ProblemDetails`, `Categories_ReturnOnlyActiveOrderedCategoriesForTargetStore`, `Products_ExcludeDraftArchivedInactiveCategoryAndOtherStoreProducts_AndReactivateCleanly`, `Products_SearchFilterSortAndPagination_BehaveDeterministically`, `Inventory_ProjectsAvailabilityFromOnHandMinusReservedAndMissingRow`, `InvalidQueryParameters_Return400ValidationProblemDetails`, `SearchOverMaxLength_Returns400ValidationProblemDetails`, `ConcurrentRequests_ForDifferentStoreSlugs_RemainStrictlyIsolated`, `ProductionHost_CatalogErrorsDoNotLeakInternalDetails`.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Evidence Status:** `Implemented & Verified` at commit `04392c875c275e99f4b5ee378f63b3a8b8fc541d` (CI Run `45622486827`).

---

### UC-07 — Manage Shopping Cart

#### 1. Identity & Metadata
- **ID:** `UC-07`
- **Stable Name:** Manage Shopping Cart (`إدارة سلة التسوق`)
- **Short Goal:** Allow a Customer to add, update, and remove published products from a single active store in a store-scoped cart prior to checkout.
- **Scope / Boundary:** Store-Scoped Cart Module.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Not Implemented` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Cart & Checkout Module
- **Source:** Phase 03 §4 (`UC-07`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Customer`.
- **Supporting Actors:** `System` (store binding, stock check, estimated subtotal calculation).
- **Actor Authority & Trust Boundary:** Customer scoped to a single `StoreId`. Cart cannot mix products from multiple stores (`AC-04`), and cart display prices are *provisional estimates* — never authoritative for final order totals (`UC-C03`).

#### 3. Stakeholder Interests & Success Guarantee
- **Customer:** Wants to build and adjust a cart of items from the current store.
- **Platform:** Wants single-store cart integrity and no reliance on client-submitted prices.
- **Success Guarantee:** Cart exists for `(Customer/Session, StoreId)` containing positive quantities of `Published` products from that store only.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Customer selects a product and clicks "Add to Cart" or opens their Cart.
- **Preconditions:**
  - Store is `Active`.
  - Target product is `Published` in an active category of the current store (`PR-02`).

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Invalid quantity (`<= 0`), unpublished product, or cross-store product is rejected without corrupting existing cart items.
- **Success Postconditions:** Cart holds updated items and provisional subtotal for a single store, ready for `UC-08`.

#### 6. Main Success Scenario
1. Customer opens a product page in an active store.
2. Customer selects a positive quantity and adds the product to the cart.
3. System validates product status, store membership, and current available stock, then updates cart items.
4. Customer updates item quantities or removes an item.
5. System displays updated cart items and server-calculated estimated subtotal.

#### 7. Alternate & Exception/Error Flows
- **7a. Non-Positive Quantity (`<= 0`) (at Step 2/4):**
  - *Outcome:* Rejected with validation error (or item removed if explicit remove action).
- **7b. Requested Quantity Exceeds Current Available Stock (`OnHand - Reserved`) (at Step 2/4):**
  - *Outcome:* Rejected; cart does not silently reserve stock (reservation occurs atomically at `UC-08`).
- **7c. Cross-Store Product Addition Attempt (at Step 2):**
  - *Outcome:* Rejected (`AC-04`, `BR-007`); cart is strictly bound to one `StoreId`.
- **7d. Price Changed While Item Is in Cart:**
  - *Outcome:* Cart price is treated as provisional only; `UC-08` re-reads authoritative price at checkout (`UC-C03`).

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-023`, `FR-024`, `FR-025`
- **Non-Functional Requirements:** `NFR-002`, `NFR-007`
- **Business Rules & Invariants:** `BR-002`, `BR-005`, `BR-006`, `BR-007`, `PR-02`, `IN-03`, `REL-01`
- **Acceptance Criteria:** `AC-01`, `AC-03`, `AC-04`, `AC-05`
- **Cross-Cutting Rules:** `UC-C02`, `UC-C03`, `UC-C04`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Cart belongs to `(StoreId, CustomerId)` (or store-scoped guest cart if supported post-design; checkout requires authenticated customer per `UC-08`).
- Cannot read/modify another customer's or another store's cart.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- Cart operations do **not** reserve inventory (`Reserved` changes only at `UC-08`).
- Server recalculates estimated subtotal from current `Product.Price` in DB.

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-07 Manage Shopping Cart
  Scenario: Cannot add product from Store B into Store A cart
    Given a customer has a cart in Store "store-a"
    When the customer attempts to add a product belonging to Store "store-b"
    Then the addition is rejected by single-store cart rule BR-007 / AC-04
    And the cart in "store-a" remains unchanged
```
- **Planned Negative/Abuse Tests:** Quantity `<= 0`, quantity > available stock, client-supplied price override attempt, cross-store product injection, `Draft` product addition.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Dependencies:** Customer Identity (`UC-05` / Gate 3) and Cart/Order persistence schema (Gate 2 / Gate 4).
- **Evidence Status:** `Not Implemented` (`0%` in `src/`).

---

### UC-08 — Checkout & Create COD Order

#### 1. Identity & Metadata
- **ID:** `UC-08`
- **Stable Name:** Checkout & Create COD Order (`إتمام الشراء وإنشاء الطلب`)
- **Short Goal:** Atomically convert a validated single-store cart into a persisted Cash-on-Delivery (`COD`) order with price/name snapshots, delivery fee verification, idempotency protection, and inventory reservation.
- **Scope / Boundary:** Store-Scoped Checkout & Order Creation.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Not Implemented` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Orders & Inventory Modules
- **Source:** Phase 03 §4 (`UC-08`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Customer` (authenticated within target `StoreId`).
- **Supporting Actors:** `System` (server-side re-verification, atomic transaction, concurrency control, idempotency).
- **Actor Authority & Trust Boundary:** Authenticated `Customer` for current `StoreId`. Client cannot supply prices, totals, delivery fees, or order status (`UC-C03`, `UC-C06`).

#### 3. Stakeholder Interests & Success Guarantee
- **Customer:** Wants transparent final pricing, reliable stock reservation, and no duplicate orders on network retries.
- **Merchant Owner:** Wants accurate COD orders with immutable product/price snapshots and guaranteed non-negative stock.
- **Success Guarantee:** Order is persisted with `OrderItem` snapshots (`ProductName`, `UnitPrice`), server-computed totals, `OrderStatus = Pending`, `PaymentMethod = COD`, and `InventoryItem.Reserved` incremented atomically in one transaction.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Customer initiates Checkout from a non-empty cart.
- **Preconditions:**
  - Customer is authenticated in the current `StoreId`.
  - Store is `Active` (`ST-02`).
  - Cart is non-empty and belongs exclusively to `StoreId`.

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** If stock is insufficient, price changed without confirmation, delivery zone is unsupported, or transaction fails, **no** order is created and **no** inventory is reserved (`TX-01`, `UC-C05`).
- **Success Postconditions:** Order + `OrderItems` (with immutable snapshots) + `InventoryReservation` committed atomically; order number and status returned.

#### 6. Main Success Scenario
1. Customer opens Checkout and reviews cart items.
2. Customer selects fulfillment method: `Delivery` or `Store Pickup` (only if `Physical` and `PickupEnabled == true`).
3. If `Delivery`, Customer provides delivery address / zone.
4. System validates delivery zone coverage and calculates delivery fee.
5. Server re-reads all products, publication status, authoritative prices, and current inventory (`OnHand`, `Reserved`) from DB.
6. Server recalculates product subtotal, delivery fee, and final total (`MNY-01`).
7. System displays final total and Customer confirms order (with `Idempotency-Key` or equivalent).
8. System creates `Order` and `OrderItems` with immutable snapshots of `ProductName` and `UnitPrice`.
9. System atomically reserves inventory (`Reserved += Qty`, ensuring `Reserved <= OnHand`) in the same transaction.
10. System clears/completes cart and returns Order number and initial status.

#### 7. Alternate & Exception/Error Flows
- **7a. Product Price Changed Since Cart Addition (at Step 5–7):**
  - *Outcome:* System returns recalculated price and requires Customer re-confirmation before creating order (`AC-05`).
- **7b. Insufficient Inventory (`OnHand - Reserved < Qty`) (at Step 5 or 9):**
  - *Outcome:* Transaction aborts; order is **not** created (`AC-06`).
- **7c. Unsupported Delivery Zone (at Step 4):**
  - *Outcome:* `Delivery` is blocked with clear validation error.
- **7d. `Store Pickup` Unavailable (`OnlineOnly` or `PickupEnabled == false`) (at Step 2):**
  - *Outcome:* Pickup option is hidden and rejected server-side if submitted (`FR-041`, `BR-010`, `ST-03`).
- **7e. Concurrent Last-Unit Purchase (at Step 9):**
  - *Outcome:* Concurrency control (`INV-01` / `IN-02`) ensures only one request reserves the last unit; the second fails cleanly without negative availability (`AC-06`).
- **7f. Duplicate Checkout Submission / Retry (at Step 7–8):**
  - *Outcome:* Handled via `Idempotency-Key` (or equivalent idempotency mechanism) to prevent duplicate orders and double reservations.

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-026`, `FR-027`, `FR-028`, `FR-029`, `FR-030`, `FR-031`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-003`, `NFR-004`, `NFR-005`, `NFR-008`
- **Business Rules & Invariants:** `BR-002`, `BR-006`, `BR-007`, `BR-008`, `BR-009`, `ST-03`, `PR-02`, `IN-01`, `IN-02`, `IN-04`, `OR-01`, `OR-02`, `OR-03`, `MNY-01`, `TX-01`, `INV-01`, `REL-01`
- **Acceptance Criteria:** `AC-01`, `AC-04`, `AC-05`
- **Cross-Cutting Rules:** `UC-C01`..`UC-C07`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Order, OrderItems, and InventoryReservations belong strictly to `StoreId` and `CustomerId`.
- Server validates `Customer.StoreId == Order.StoreId == Product.StoreId`.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Transaction Boundary (`TX-01`):** Order creation, snapshot persistence, and `InventoryItem.Reserved` increment execute in a single ACID transaction.
- **Concurrency (`INV-01`):** Pessimistic lock or optimistic concurrency (`RowVersion`) + SQL `CK_InventoryItems_ReservedWithinOnHand` prevents overselling.
- **Idempotency:** Duplicate checkout requests with the same `Idempotency-Key` return the original order without re-reserving stock.
- **Audit Event:** `OrderCreated` (`OrderId`, `StoreId`, `CustomerId`, `TotalAmount`, timestamp).

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-08 Checkout & Create COD Order
  Scenario: Concurrent checkout for the last available unit
    Given product "espresso-beans" has OnHand = 1 and Reserved = 0
    When Customer A and Customer B concurrently submit Checkout for quantity 1 each
    Then exactly one order succeeds with Reserved = 1
    And the other checkout fails with insufficient stock
    And Available quantity never becomes negative

  Scenario: Server ignores client-submitted price and uses authoritative DB price
    Given product "espresso-beans" has DB price 10.000 JOD
    When Customer submits Checkout claiming unit price 1.000 JOD
    Then the server computes totals using 10.000 JOD (or rejects tampered payload)
```
- **Planned Negative/Abuse Tests:** Concurrent last-unit race (`AC-06`), idempotent retry (`Idempotency-Key`), price tampering, unsupported delivery zone, `OnlineOnly` pickup injection, unauthenticated checkout.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Open Decisions:** `OD-001` (Reservation expiry policy), `OD-004` (Delivery GeoAreas & fees in Jordan).
- **Out-of-Scope:** Online card/wallet payment gateways (MVP is `COD` only per `FR-029`).
- **Evidence Status:** `Not Implemented` in `src/` (only `InventoryItem` SQL constraints `IN-01`/`IN-02` exist as foundation).

---

### UC-09 — Manage Order Fulfillment & Delivery/Pickup

#### 1. Identity & Metadata
- **ID:** `UC-09`
- **Stable Name:** Manage Order Fulfillment & Delivery/Pickup (`إدارة الطلب وتجهيزه وتسليمه`)
- **Short Goal:** Allow a Merchant Owner to process an order through valid lifecycle, fulfillment, and COD payment states up to `Delivered` or `PickedUp`.
- **Scope / Boundary:** Store-Scoped Order & Fulfillment Lifecycle.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Not Implemented` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Orders & Fulfillment Module
- **Source:** Phase 03 §4 (`UC-09`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Merchant Owner` (authorized for target `StoreId`).
- **Supporting Actors:** `System` (state machine validation, audit logging).
- **Actor Authority & Trust Boundary:** Authenticated Merchant Owner scoped strictly to their `StoreId`. Cannot modify orders of another store or skip required state transitions (`UC-C06`).

#### 3. Stakeholder Interests & Success Guarantee
- **Merchant Owner:** Wants structured tracking of order confirmation, preparation, dispatch/pickup readiness, and COD collection.
- **Customer:** Wants accurate order status visibility.
- **Success Guarantee:** Order transitions strictly along valid `OrderStatus`, `FulfillmentStatus`, and `PaymentStatus` state machines with full actor/timestamp audit history.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Merchant Owner opens the store's Order Management dashboard.
- **Preconditions:**
  - Order exists and belongs to the Merchant's `StoreId`.
  - Merchant Owner is authenticated and authorized for `StoreId`.

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Disallowed state transitions or cross-store mutations are rejected with zero state change.
- **Success Postconditions:** Order reaches completed state (`Delivered` or `PickedUp`) or intermediate valid state, with `PaymentStatus` recorded separately (`COD` collected) and audit trail persisted.

#### 6. Main Success Scenario
1. Merchant Owner opens the Orders dashboard.
2. Merchant Owner selects a new order and reviews items, fulfillment type, and totals.
3. Merchant Owner confirms (or rejects per policy) the order.
4. Upon confirmation, preparation starts and `FulfillmentStatus` updates.
5. For `Delivery`, Merchant records `OutForDelivery` and then `Delivered`.
6. For `Pickup`, Merchant records `ReadyForPickup` and then `PickedUp` upon customer collection.
7. System records every state transition with timestamp and acting principal (`UC-C07`), updating `PaymentStatus` separately upon COD collection.

#### 7. Alternate & Exception/Error Flows
- **7a. Disallowed State Transition (e.g. `Pending` -> `Delivered` or `PickedUp` before `ReadyForPickup`) (at Step 3–6):**
  - *Outcome:* Rejected by domain state machine (`OR-04`, `OR-05`, `UC-C06`).
- **7b. Cross-Store Order Access Attempt:**
  - *Outcome:* Rejected (`AC-01`, `UC-C02`).
- **7c. COD Payment Collection:**
  - *Outcome:* Recorded independently in `PaymentStatus` (`OR-06`) rather than conflated with `FulfillmentStatus`.

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-032`, `FR-033`, `FR-034`, `FR-035`, `FR-037`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-008`
- **Business Rules & Invariants:** `BR-002`, `BR-010`, `OR-04`, `OR-05`, `OR-06`, `OR-07`, `REL-01`
- **Acceptance Criteria:** `AC-01`
- **Cross-Cutting Rules:** `UC-C01`, `UC-C02`, `UC-C06`, `UC-C07`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Order and its status history belong to `StoreId`.
- Only authorized `Merchant Owner` of that `StoreId` can execute fulfillment transitions.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Inventory Commitment:** Final deduction from `OnHand` and clearing of `Reserved` at fulfillment completion must obey `IN-01`/`IN-02`/`IN-04` atomically.
- **Audit Events:** `OrderConfirmed`, `OrderOutForDelivery`, `OrderReadyForPickup`, `OrderDelivered`, `OrderPickedUp`, `CodPaymentCollected`.

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-09 Manage Order Fulfillment & Delivery/Pickup
  Scenario: Pickup order cannot be marked PickedUp before ReadyForPickup
    Given a Pickup order is in "Confirmed" / "Preparing" state
    When the Merchant Owner attempts to transition it directly to "PickedUp"
    Then the transition is rejected by domain rule OR-05
```
- **Planned Negative/Abuse Tests:** Invalid state jump (`Pending` -> `Delivered`), marking `Delivery` order as `PickedUp`, cross-store order status update.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Open Decisions:** `OD-003` (Allowed merchant rejection/cancellation reasons), `OD-005` (Minimum Proof-of-Delivery evidence at `Delivered`/`PickedUp`).
- **Evidence Status:** `Not Implemented` in `src/`.

---

### UC-10 — Cancel Order & Release Inventory

#### 1. Identity & Metadata
- **ID:** `UC-10`
- **Stable Name:** Cancel Order & Release Inventory (`إلغاء الطلب ومعالجة المخزون`)
- **Short Goal:** Allow a permitted Customer or Merchant Owner to cancel an eligible order and release its active inventory reservation **exactly once**.
- **Scope / Boundary:** Store-Scoped Order Cancellation & Inventory Compensation.
- **Level:** User Goal
- **Lifecycle Status:** `Approved` (Specification) / `Not Implemented` (Code)
- **Version:** `1.0`
- **Owner:** Product Owner / Orders & Inventory Modules
- **Source:** Phase 03 §4 (`UC-10`)

#### 2. Actors & Trust Boundaries
- **Primary Actor:** `Customer` (for own order within allowed cutoff) or `Merchant Owner` (for store order per policy).
- **Supporting Actors:** `System` (cancellation policy check, atomic reservation release, idempotency).
- **Actor Authority & Trust Boundary:** Customer can cancel only their own order in target `StoreId`; Merchant Owner can cancel only orders in their `StoreId`. Neither can cancel a completed order (`OR-08`).

#### 3. Stakeholder Interests & Success Guarantee
- **Customer / Merchant:** Wants eligible orders cancelled cleanly with accurate reason tracking.
- **Inventory Integrity:** Wants reserved stock returned to available stock **exactly once**, even under concurrent cancellation attempts (`FR-037`, `BR-009`, `IN-04`, `OR-08`).
- **Success Guarantee:** Order transitions to `Cancelled`, active reservation is marked released and `InventoryItem.Reserved` is decremented **once**, with reason and actor logged.

#### 4. Trigger & Explicit Preconditions
- **Trigger:** Customer or Merchant Owner requests cancellation of an order.
- **Preconditions:**
  - Order exists in the current `StoreId`.
  - Order status permits cancellation under policy (not `Delivered`, `PickedUp`, or already `Cancelled`).

#### 5. Minimal/Failure Guarantee & Success Postconditions
- **Minimal / Failure Guarantee:** Disallowed or duplicate cancellation attempts do **not** modify `InventoryItem.Reserved` twice (`IN-04`, `BR-012`).
- **Success Postconditions:** Order status is `Cancelled`, reservation released once, `Reserved >= 0`, and audit event logged.

#### 6. Main Success Scenario
1. Customer or Merchant Owner requests order cancellation (with reason where required).
2. System verifies actor permission, current order state, and cancellation policy.
3. System records cancellation reason and acting principal.
4. System transitions order status to `Cancelled`.
5. System releases the active inventory reservation linked to the order **exactly once** within the same transaction.
6. System logs the state transition and reservation release.

#### 7. Alternate & Exception/Error Flows
- **7a. Completed Order (`Delivered` or `PickedUp`) (at Step 2):**
  - *Outcome:* Cancellation rejected (`OR-08`).
- **7b. Already `Cancelled` Order (at Step 2 or Concurrent Request):**
  - *Outcome:* Idempotent/safe rejection or no-op; inventory is **never** released twice (`FR-037`, `BR-009`, `IN-04`, `OR-08`).
- **7c. Concurrent Cancellation Attempts (at Step 4–5):**
  - *Outcome:* Concurrency guard ensures only the first transition releases stock (`BR-012`, `IN-04`, `TX-01`).
- **7d. Cancellation Requested During Preparation or Out-for-Delivery (at Step 2):**
  - *Outcome:* Governed by `OD-002` (Customer cancellation cutoff policy pending Product Owner decision).

#### 8. Linked Rules, Requirements & Acceptance Criteria
- **Functional Requirements:** `FR-036`
- **Non-Functional Requirements:** `NFR-001`, `NFR-002`, `NFR-003`, `NFR-004`, `NFR-008`
- **Business Rules & Invariants:** `BR-002`, `BR-011`, `BR-012`, `IN-01`, `IN-02`, `IN-04`, `OR-08`, `OR-09`, `TX-01`, `INV-01`, `REL-01`
- **Acceptance Criteria:** `AC-02`, `AC-06` (Reservation release contract traced via `FR-037`, `BR-009`, `IN-04`, `OR-08`)
- **Cross-Cutting Rules:** `UC-C01`, `UC-C02`, `UC-C05`, `UC-C06`, `UC-C07`

#### 9. Data Ownership, Tenant Scope & Authorization Condition
- Order and InventoryReservation belong to `StoreId`.
- Customer A cannot cancel Customer B's order; Merchant A cannot cancel Store B's order.

#### 10. Side Effects, Consistency, Concurrency/Idempotency & Audit Events
- **Atomicity (`TX-01`):** `OrderStatus = Cancelled`, `InventoryReservation.Released`, and `InventoryItem.Reserved -= Qty` execute atomically.
- **Exactly-Once Release (`OR-09`):** Enforced via reservation status flag + optimistic concurrency / atomic update so concurrent cancel calls cannot double-decrement `Reserved`.
- **Audit Event:** `OrderCancelled` (`OrderId`, `ActorId`, `ActorRole`, `Reason`, `ReleasedReservationId`, timestamp).

#### 11. Acceptance Examples & Planned Test Scenarios
```gherkin
Feature: UC-10 Cancel Order & Release Inventory
  Scenario: Concurrent cancellations release reserved inventory exactly once
    Given an Order for 2 units of product "espresso-beans" has an active reservation (OnHand = 5, Reserved = 2)
    When two cancellation requests arrive concurrently for that Order
    Then the Order status becomes "Cancelled"
    And InventoryItem.Reserved becomes 0 (not -2)
```
- **Planned Negative/Abuse Tests:** Cancel `Delivered` order, cancel already-`Cancelled` order twice sequentially and concurrently (`FR-037` / `BR-009`), Customer B cancelling Customer A's order.

#### 12. Open Decisions, Assumptions, Out-of-Scope Cases, Dependencies & Evidence Status
- **Open Decisions:** `OD-002` (Customer self-cancellation cutoff stage), `OD-003` (Merchant cancellation reasons).
- **Evidence Status:** `Not Implemented` in `src/`.

---

## 3. Summary of Open Decisions Affecting Use Cases

All unresolved policy decisions from Phase 03 §8 and Gate 1 analysis are tracked formally in [Change-Log-and-Open-Decisions.md](../08-Engineering-Management/Change-Log-and-Open-Decisions.md) (`OD-001`–`OD-008`) and must not be hardcoded before Product Owner approval.
