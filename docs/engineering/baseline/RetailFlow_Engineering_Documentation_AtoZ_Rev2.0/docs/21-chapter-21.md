# 21. المصطلحات والمراجع وسجل المصدر

## 21.1 Glossary

| المصطلح | المعنى في RetailFlow |
|---|---|
| Tenant / Store | مساحة متجر مستقلة ضمن منصة مشتركة |
| StoreId | معرف المتجر الداخلي؛ مفتاح ملكية وعزل |
| Store Context | المتجر الموثوق المرتبط بالطلب الحالي |
| Modular Monolith | تطبيق واحد بوحدات داخلية وحدود واضحة |
| Aggregate | حد اتساق معاملات المجال |
| Invariant | قاعدة يجب أن تبقى صحيحة دائماً |
| Reservation | حجز كمية مخزون لطلب وفق حالة ومدة وسياسة |
| Idempotency | تكرار الطلب بنفس المفتاح لا يكرر الأثر التجاري |
| Outbox | تخزين حدث/رسالة مع التغيير التجاري لإرسال موثوق لاحقاً |
| SLI | مؤشر مستوى الخدمة المقاس |
| SLO | هدف مستوى الخدمة |
| RPO | أقصى نافذة فقد بيانات مقبولة |
| RTO | الوقت المستهدف لاستعادة الخدمة |
| ADR | سجل قرار معماري مع سياقه وبدائله وحالته |
| Evidence | دليل قابل للمراجعة على تحقق معيار قبول |

## 21.2 مراجع المعايير والممارسات

هذه المراجع استُخدمت كأطر استرشاد ومقارنة، ولا تمثل شهادة مطابقة للمشروع:

1. ISO/IEC/IEEE 29148:2018 — Requirements Engineering: https://www.iso.org/standard/72089.html
2. ISO/IEC/IEEE 42010:2022 — Architecture Description: https://www.iso.org/standard/74393.html
3. arc42 — Software Architecture Documentation Template: https://docs.arc42.org/
4. C4 Model — Architecture Diagrams: https://c4model.com/
5. OWASP Application Security Verification Standard 5.0: https://asvs.owasp.org/
6. OWASP Cheat Sheet Series: https://cheatsheetseries.owasp.org/
7. Microsoft Azure Well-Architected Framework: https://learn.microsoft.com/azure/well-architected/
8. Microsoft SaaS multitenant solution architecture: https://learn.microsoft.com/azure/architecture/guide/saas-multitenant-solution-architecture/
9. Microsoft ASP.NET Core security, authentication, antiforgery, rate limiting, health checks and proxy guidance: https://learn.microsoft.com/aspnet/core/
10. Microsoft EF Core transactions and concurrency guidance: https://learn.microsoft.com/ef/core/saving/transactions
11. OpenAPI Specification: https://spec.openapis.org/oas/latest.html
12. NIST Secure Software Development Framework (SSDF): https://csrc.nist.gov/Projects/ssdf

## 21.3 ملفات المصدر

| المصدر | كيفية استخدامه |
|---|---|
| 01 Project Introduction | Product Vision، MVP scope، persona/role، original 8-week indicative plan |
| 02 Vision & Functional Requirements | FR-001..FR-042، NFR، BR-001..BR-012، acceptance baseline |
| 03 Use Case Catalogue | UC-01..UC-10، actors، flows، exceptions |
| 04 Domain Model & Business Rules | modules، entities، StoreId، inventory/order rules، aggregates |
| 05 Database Design Revision 1.1 | proposed schema، persistence، composite FKs، CSRF/session alignment addendum |
| 06 Identity Study Revision 1.2 | Identity/Session PoCs، CSRF decision and open production gates |
| 07 Product Host Integration | Phase 07 proposed execution plan and Definition of Done |
| Git commit 04392c875c275e99f4b5ee378f63b3a8b8fc541d | inspected implementation baseline; see `evidence/baseline-evidence.md` |

## 21.4 Change Control

عند تغير قرار أو متطلب:
1. افتح change request يصف السبب والأثر.
2. حلل أثر التغيير على FR/UC/BR/ADR/API/DB/tests/security/deployment.
3. حدد هل التغيير breaking أو additive.
4. احصل على موافقة owner المناسب.
5. حدث مصدر الوثيقة والـ registers والـ diagrams والاختبارات في نفس change set.
6. سجّل version وcommit وreview evidence.

---

**نهاية الدليل الرئيسي.** حالة هذا الإصدار: Engineering Baseline Draft 2.0. لا يمثل موافقة Product Owner النهائية، ولا شهادة ISO/OWASP، ولا إعلان Production Readiness.
