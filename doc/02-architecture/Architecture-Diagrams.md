# مخططات المعمارية والتدفقات

المخططات التالية تصف المسارات الظاهرة في Product Host عند baseline. تم استخدام C4 للمكونات الثابتة، وSequence/State diagrams للتدفقات والسلوك.

## System Context (C4 Level 1)

```mermaid
flowchart LR
  C[Customer / Browser Client]
  M[Merchant Owner / Browser Client]
  P[Platform Admin / Browser Client]
  RF[RetailFlow Product Host\nASP.NET Core Minimal API]
  DB[(SQL Server\nRetailFlowDbContext)]
  C -->|HTTP + Cookie| RF
  M -->|HTTP + Cookie| RF
  P -->|HTTP + Cookie| RF
  RF -->|EF Core| DB
```

لا يُرسم Frontend مستقل لأن شجرة هذا commit لا تحتوي تطبيق Client منفصل.

## Container / Project View (C4 Level 2 style)

```mermaid
flowchart TB
  subgraph Host[RetailFlow Product Host]
    API[RetailFlow.Api\nHTTP endpoints + middleware]
    APP[RetailFlow.Application\nContracts + interfaces]
    DOM[RetailFlow.Domain\nEntities + invariants]
    INF[RetailFlow.Infrastructure\nServices + EF Core]
  end
  SQL[(SQL Server)]
  API --> APP
  API --> INF
  APP --> DOM
  INF --> APP
  INF --> DOM
  INF --> SQL
```

## Module View

```mermaid
flowchart LR
  API[API endpoints]
  S[Stores / Tenancy]
  I[Identity / Sessions]
  C[Catalog]
  O[Cart / Checkout / Orders]
  F[Fulfillment]
  D[(SQL Server)]
  API --> S
  API --> I
  API --> C
  API --> O
  O --> C
  O --> F
  S --> D
  I --> D
  C --> D
  O --> D
  F --> D
```

## Customer checkout sequence (observed)

```mermaid
sequenceDiagram
  actor Customer
  participant API as CustomerOrderEndpoints
  participant Checkout as CheckoutService
  participant DB as SQL Server
  Customer->>API: POST /checkout + Idempotency-Key
  API->>Checkout: CheckoutAsync(storeId, customerId, key, command)
  Checkout->>DB: Read Cart
  Checkout->>Checkout: Compute SHA-256 request hash
  Checkout->>DB: Insert CheckoutRequest(InProgress) + SaveChanges
  alt Unique key collision
    Checkout->>DB: Load existing key
    Checkout-->>API: Replayed / Conflict / PayloadMismatch / Failed
  else Key claim acquired
    Checkout->>DB: Load Store, Zone, Products, Inventory
    Checkout->>Checkout: Reserve inventory; create Order + OrderItems
    Checkout->>DB: Save order, inventory, clear cart, mark claim Completed
    Checkout-->>API: Created order
  end
  API-->>Customer: 201 / 200 / 409 / 422 / 400
```

## Order lifecycle (separate axes)

```mermaid
stateDiagram-v2
  state "OrderStatus" as OS {
    [*] --> Pending
    Pending --> Confirmed: merchant confirm
    Pending --> Rejected: merchant reject
    Pending --> Cancelled: customer cancel / timeout
    Confirmed --> Cancelled: merchant or timeout where allowed
    Confirmed --> Completed: COD collected after Delivered/PickedUp
    Rejected --> [*]
    Cancelled --> [*]
    Completed --> [*]
  }
```

FulfillmentStatus is tracked separately from OrderStatus. The domain transition rules are in `Order.AdvanceFulfillment`; do not merge them into one state machine.

## Store Lifecycle

```mermaid
stateDiagram-v2
  [*] --> PendingApproval
  PendingApproval --> Active: Approve
  PendingApproval --> Rejected: Reject
  Active --> Suspended: Suspend
  Suspended --> Active: Approve / reactivate
```

**Source anchors:** `Store.cs`, `Order.cs`, `CheckoutService.cs`, `ProductHostExtensions.cs`, endpoint modules.
