# ADR-007: Commercial MVP Scope, Same-Origin React Hosting, and COD Extensibility

**Status:** Accepted (2026-11-26)  
**Date:** 2026-11-26  
**Owner / Approver:** Project Owner

## Context
RetailFlow is currently an ASP.NET Core `net10.0` Minimal API Product Host with no React/TypeScript client source in the repository yet, and uses Cash on Delivery (`COD`) as its payment path. Clear product and hosting boundaries are required so future implementation and documentation stay aligned.

## Decision
1. **Commercial MVP Scope:** Build a production-safe commercial MVP, not a demo and not a full feature-complete enterprise suite. Distinguish **Required for MVP**, **Required for Production Safety**, **Post-MVP**, and **Future Architecture Extension**.
2. **Same-Origin React Hosting (Target Architecture):** The future React SPA will be hosted inside the ASP.NET Core Product Host using Same-Origin architecture (`Browser -> ASP.NET Core Product Host -> React SPA + API + Auth/AuthZ + Application + Domain + Infrastructure -> SQL Server`). React is **Target Architecture** until `.tsx`/`.ts` source files exist in the repository.
3. **COD for MVP with Extensible Payment Concepts:** `COD` is the approved payment method for MVP; do not implement an online payment gateway in this phase, while keeping `PaymentMethod` / `PaymentStatus` (`Pending`, `Collected`, `Voided`) separated from `OrderStatus` and `FulfillmentStatus` so future payment providers can be added without breaking the core order lifecycle.

## Consequences & Risks
- Same-Origin hosting avoids wide cross-origin CORS (`AllowAnyOrigin`/`AllowCredentials`) and leverages `__Host-RetailFlow.Session` `SameSite=Lax` cookies plus `IAntiforgery` synchronizer tokens when the React SPA is added.
- Keeping payment axes separated in `Order` prevents over-engineering in MVP while preserving future gateway extensibility.

## Evidence & References
- `CODE:` `src/RetailFlow.Api/Program.cs` (SPA fallback), `src/RetailFlow.Domain/Orders/Order.cs` (`OrderStatus`, `FulfillmentStatus`, `PaymentStatus`), `src/RetailFlow.Infrastructure/Orders/OrderService.cs` (`CollectCodPaymentAsync`).
- Related: [ADR-001](./ADR-001-Layered-Product-Host.md), [ADR-004](./ADR-004-Cookie-Authentication-and-Server-Side-Tickets.md), `doc/10-open-items/Production-Gaps-and-Decision-Backlog.md` (`GAP-012`, `GAP-016`).
