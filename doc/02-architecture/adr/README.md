# Architecture Decision Records (ADR)

## ملاحظة تاريخية

السجلات في هذه الحزمة **Inferred / Needs Confirmation**؛ أُعيد بناؤها من شكل التنفيذ الحالي بعد تجاهل التوثيق السابق، ولا تدّعي أنها قرارات اتخذها الفريق في تاريخ معلوم. يجب على مالك الهندسة اعتمادها أو استبدالها بسجل رسمي بعد مراجعة البدائل والسياق التجاري.

## القائمة

| ID | الموضوع | الحالة الحالية في الوثيقة |
|---|---|---|
| [ADR-001](./ADR-001-Layered-Product-Host.md) | Layered Product Host | Inferred from solution references — Needs Approval |
| [ADR-002](./ADR-002-SQL-Server-and-EF-Core.md) | SQL Server + EF Core | Inferred from provider and DbContext — Needs Approval |
| [ADR-003](./ADR-003-Store-Scoped-Tenancy.md) | Store-scoped tenancy | Inferred from route scope and data model — Needs Approval |
| [ADR-004](./ADR-004-Cookie-Authentication-and-Server-Side-Tickets.md) | Cookie auth + server-side session tickets | Updated by ADR-006 & reconciled with `SqlSessionTicketStore` |
| [ADR-005](./ADR-005-Checkout-Idempotency-and-Inventory-Reservation.md) | Checkout idempotency + stock reservation | Inferred from code — Reconciled with lease/cart-hash/worker |
| [ADR-006](./ADR-006-ASP.NET-Core-Identity-Adoption.md) | ASP.NET Core Identity adoption | Owner Decision Implemented — Pending Final Sign-off |
| [ADR-007](./ADR-007-Commercial-MVP-Same-Origin-React-and-COD.md) | Commercial MVP scope, Same-Origin React target & COD | Accepted (2026-11-26) |

قالب إضافة قرار جديد: [`ADR-TEMPLATE.md`](./ADR-TEMPLATE.md).

## متى نضيف ADR؟

عند تغيير authentication/session, tenant isolation, persistence, transaction boundaries, API versioning, hosting, integrations, or deployment topology. كل ADR يجب أن يحتوي Context, Decision, Alternatives, Consequences, Risks, Evidence, Status.
