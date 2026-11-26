# ADR-0005 — Separate Order, Fulfillment, and Payment State Models

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-11-26 |
| Owner | Product Owner & Principal Software Architect |
| Decision scope | Architecture / Data / Domain |
| Related requirements | `FR-027`–`FR-037`, `BR-009`, `BR-011`, `OR-01`, `OR-05`, `OR-08`, `UC-08`, `UC-09`, `UC-10`, `AUD-004` |
| Supersedes | None |
| Superseded by | None |

## 1. Context
Gate 1 and Documentation-to-Code Audit finding `AUD-004` identified that earlier drafts conflated order lifecycle, physical fulfillment/pickup progression, and Cash-on-Delivery (COD) payment collection into a single coarse status field. RetailFlow supports both `Delivery` and `Pickup` (`ST-03`, `BR-011`) and COD MVP settlement (`FR-035`), which progress on distinct axes and have distinct failure/cancellation semantics.

## 2. Problem statement
How should RetailFlow model order, fulfillment, and payment states and enforce valid cross-state transitions before implementing Cart, Checkout, and Order Management?
- **Explicitly out of scope (unresolved PO policies):**
  - `OD-001`: Reservation expiry timeout duration.
  - `OD-002`: Latest stage at which a Customer may self-cancel.
  - `OD-003`: Mandatory Merchant rejection/cancellation reason taxonomy.
  - `OD-005`: Minimum Proof-of-Delivery / Pickup evidence payload.

## 3. Decision drivers and constraints
- **Correctness (`AUD-004`):** Prevent illegal combinations (e.g., marking a `Cancelled` order `Delivered`, or `Collected` payment on an unfulfilled/cancelled order).
- **Tenant isolation:** Every state transition must be scoped by `StoreId` (`OR-01`–`OR-08`).
- **Concurrency & auditability:** Optimistic concurrency (`rowversion`) and append-only `OrderStatusHistory` audit trail (`FR-034`).
- **COD MVP constraint:** Only Cash-on-Delivery / Cash-on-Pickup is in MVP scope; online payment gateways are deferred.

## 4. Evidence and research
| Source | Authority/type | Version/date | Relevant claim | Applicability/limitations |
|---|---|---|---|---|
| `docs/01-Requirements/Use-Case-Specifications.md` | Project baseline | 2026-11-26 | `UC-08`, `UC-09`, `UC-10` define checkout, merchant fulfillment, and cancellation flows | `OD-001`–`OD-003`, `OD-005` remain pending PO approval |
| `docs/08-Engineering-Management/Change-Log-and-Open-Decisions.md` | Project control | 2026-11-26 | `OD-001`–`OD-005` require Product Owner sign-off before implementation | Blocks finalization of cutoff and expiry constants |
| EF Core 10 Concurrency Tokens (`https://learn.microsoft.com/en-us/ef/core/saving/concurrency`) | Official | EF Core 10 | `IsRowVersion()` detects concurrent state updates via `DbUpdateConcurrencyException` | Requires explicit conflict handling in Application layer |

## 5. Options considered
- **Option A — Single `OrderStatus` enum:** Mixes `Pending`, `Confirmed`, `OutForDelivery`, `ReadyForPickup`, `Delivered`, `PickedUp`, `Paid`, `Cancelled`. *Rejected:* causes combinatorial explosion and cannot represent partial/independent COD collection or delivery vs pickup cleanly.
- **Option B — Three orthogonal state enums (`OrderStatus`, `FulfillmentStatus`, `PaymentStatus`) with explicit cross-state guards (Recommended):** Cleanly separates commercial order lifecycle, logistics progression, and COD settlement.

## 6. Decision
Recommend adopting three orthogonal state models on the `Order` aggregate (keeping `Status: Proposed` until PO sign-off on `OD-001`–`OD-003`, `OD-005` and Gate 2 approval):

1. **`OrderStatus`** (Commercial lifecycle):
   - `Pending` -> `Confirmed` | `Rejected` | `Cancelled`
   - `Confirmed` -> `Completed` | `Cancelled`
   - Terminal states: `Completed`, `Rejected`, `Cancelled`
2. **`FulfillmentStatus`** (Logistics lifecycle, branched by `FulfillmentMode`):
   - Common initial: `Unfulfilled`
   - `Delivery` mode: `Unfulfilled` -> `Preparing` -> `OutForDelivery` -> `Delivered` (or `Failed` / `Cancelled`)
   - `Pickup` mode: `Unfulfilled` -> `Preparing` -> `ReadyForPickup` -> `PickedUp` (or `Cancelled`)
3. **`PaymentStatus`** (COD MVP lifecycle):
   - `Pending` -> `Collected` | `Voided`
4. **Cross-state invariants:**
   - `OrderStatus == Rejected` or `Cancelled` forces `FulfillmentStatus == Cancelled` (only from non-terminal fulfillment) and `PaymentStatus == Voided` (if `Pending`), triggering `IN-04` inventory reservation release.
   - `OrderStatus == Completed` requires `FulfillmentStatus in { Delivered, PickedUp }` AND `PaymentStatus == Collected`.
   - `PaymentStatus == Collected` requires `OrderStatus == Confirmed` and `FulfillmentStatus in { Delivered, PickedUp }` (for COD).

## 7. Complete State Transition & Cross-State Invariant Model (`RF-REV-004`)

### 7.1 `OrderStatus` Transitions
| From | Action | To | Actor | Preconditions | Inventory & Payment Side Effects | Audit |
|---|---|---|---|---|---|---|
| *(Initial)* | `Order.Create` | `Pending` | `Customer` | Active store, valid items/totals (`MNY-01`) | `InventoryItem.Reserve(qty)` (`OD-001` 15-min expiry); `PaymentStatus=Pending` | `OrderStatusHistory` |
| `Pending` | `Confirm` | `Confirmed` | `MerchantOwner` | Store `StoreId` match | Reservation retained | `OrderStatusHistory` |
| `Pending` | `Reject` | `Rejected` *(Terminal)* | `MerchantOwner` | Non-empty `reason` | `InventoryItem.ReleaseReservation(qty)`; `FulfillmentStatus=Cancelled`; `PaymentStatus=Voided` | `OrderStatusHistory` |
| `Pending` | `CancelByCustomer` | `Cancelled` *(Terminal)* | `Customer` | Owning `CustomerId`, `OrderStatus == Pending` (`OD-002`) | `InventoryItem.ReleaseReservation(qty)`; `FulfillmentStatus=Cancelled`; `PaymentStatus=Voided` | `OrderStatusHistory` |
| `Pending` / `Confirmed` | `CancelByMerchantOrTimeout` | `Cancelled` *(Terminal)* | `MerchantOwner` / `System` | `FulfillmentStatus` not `Delivered`/`PickedUp` | `InventoryItem.ReleaseReservation(qty)`; `FulfillmentStatus=Cancelled`; `PaymentStatus=Voided` | `OrderStatusHistory` |
| `Confirmed` | `CollectCodPayment` | `Completed` *(Terminal)* | `MerchantOwner` | `FulfillmentStatus in {Delivered, PickedUp}` & `PaymentStatus == Pending` | `InventoryItem.CommitReservation(qty)` (`OnHand -= qty`, `Reserved -= qty`); `PaymentStatus=Collected` | `OrderStatusHistory` |

### 7.2 `FulfillmentStatus` (Delivery vs Pickup) & Cross-State Invariants
- **Delivery path:** `Unfulfilled -> Preparing -> OutForDelivery -> Delivered` (or `OutForDelivery -> Failed`).
- **Pickup path:** `Unfulfilled -> Preparing -> ReadyForPickup -> PickedUp`.
- **Forbidden Cross-State Combinations:**
  1. Cancellation before fulfillment (`Pending` -> `Cancelled`): allowed for Customer (`OD-002`) and Merchant/Timeout; releases stock.
  2. Customer cancellation after fulfillment begins (`Confirmed` / `Preparing`+): **Rejected** (`409 Conflict` per `OD-002`).
  3. Cancellation after COD collection (`PaymentStatus == Collected` / `OrderStatus == Completed`): **Prohibited in current state machine**; any future post-delivery return/refund workflow is **Pending Owner Decision** (do not invent a refund policy).
  4. `OrderStatus == Completed` with `PaymentStatus != Collected` or `FulfillmentStatus not in {Delivered, PickedUp}`: **Rejected**.
  5. `PaymentStatus == Collected` with `OrderStatus != Confirmed` or `FulfillmentStatus not in {Delivered, PickedUp}`: **Rejected**.

## 8. Consequences
- **Positive:** Eliminates ambiguous states, supports Delivery and Pickup uniformly, and enables clean unit testing of every transition matrix cell.
- **Negative:** Slightly more columns and check constraints on `Orders` and `OrderStatusHistories`.
- **Follow-on work:** Bind `OD-002` cutoff rule to the `OrderStatus`/`FulfillmentStatus` guard once PO approves.

## 8. Security and tenant isolation
- Only authenticated Merchant Owners/Staff of the owning `StoreId` may advance `Confirmed`, `Rejected`, `Preparing`, `OutForDelivery`, `ReadyForPickup`, `Delivered`, `PickedUp`, or `Collected`.
- Customers may only invoke self-cancellation on their own `CustomerId` + `StoreId` order within the `OD-002`-approved window.

## 9. Implementation plan
- Do **not** implement `Orders` tables or endpoints until Gate 2 (`AUD-004`, `AUD-005`) and `OD-001`–`OD-005` are approved.
- Once approved, implement enums, domain transition methods, `CK_Orders_*` SQL constraints, `OrderStatusHistory` audit table, and comprehensive xUnit transition tests.

## 10. Rollback / recovery
- Pure design ADR; no schema or code changes applied while `Proposed`.

## 11. Verification and acceptance
- Unit tests (`RetailFlow.Domain.UnitTests`) covering 100% of legal and illegal `(OrderStatus, FulfillmentStatus, PaymentStatus)` transitions for both `Delivery` and `Pickup` modes.
- SQL integration tests verifying `CHECK` constraints and `rowversion` `DbUpdateConcurrencyException` handling on concurrent state transitions.

## 12. Approval
- Owner decision: Accepted (`OD-001 = 15 minutes`, `OD-002 = Pending only`)
- Approved by: Project Owner (via interactive session decision)
- Approval date: 2026-11-26
- Evidence/reference: Session decision `2026-11-26T02:45:31+03:00`

## 13. Implementation status (separate from decision status)
- Not started (Blocked on Gate 2 approval and `OD-001`–`OD-005`)
- Code paths: None
- Commit: N/A
- CI run: N/A
- Remaining gaps: PO decisions `OD-001`–`OD-003`, `OD-005` and Owner acceptance of this ADR.
