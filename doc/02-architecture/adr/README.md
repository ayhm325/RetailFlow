# Architecture Decision Records (ADR)

## ملاحظة تاريخية

السجلات في هذه الحزمة **Inferred / Needs Confirmation**؛ أُعيد بناؤها من شكل التنفيذ الحالي بعد تجاهل التوثيق السابق، ولا تدّعي أنها قرارات اتخذها الفريق في تاريخ معلوم. يجب على مالك الهندسة اعتمادها أو استبدالها بسجل رسمي بعد مراجعة البدائل والسياق التجاري.

## القائمة

| ID | الموضوع | الحالة الحالية في الوثيقة |
|---|---|---|
| ADR-001 | Layered Product Host | Inferred from solution references |
| ADR-002 | SQL Server + EF Core | Inferred from provider and DbContext |
| ADR-003 | Store-scoped tenancy | Inferred from route scope and data model |
| ADR-004 | Cookie auth + server-side session tickets | Inferred from host registration |
| ADR-005 | Checkout idempotency + stock reservation | Inferred from code and DB constraints |

## متى نضيف ADR؟

عند تغيير authentication/session, tenant isolation, persistence, transaction boundaries, API versioning, hosting, integrations, or deployment topology. كل ADR يجب أن يحتوي Context, Decision, Alternatives, Consequences, Risks, Evidence, Status.
