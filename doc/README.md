# RetailFlow — حزمة التوثيق الهندسي

**الإصدار:** 1.0.0 (Initial Code-Derived Baseline)  
**تاريخ إعداد الحزمة:** 2026-11-26  
**المستودع:** https://github.com/ayhm325/RetailFlow  
**الفرع المرجعي:** `main`  
**Commit المرجعي:** `b279883a664e5f515a1049fbf7055865d6f5b662`  
**حالة الوثائق:** Draft — Code-derived, not an approved product specification

> هذه الحزمة أُنشئت انطلاقاً من الكود وإعدادات البناء والاختبارات في الـ commit أعلاه. لم يتم استخدام ملفات التوثيق الموجودة في المستودع كمصدر للحقيقة. عند التعارض، الكود هو دليل التنفيذ، والاختبارات دليل سلوك مختبَر، والمراجع الرسمية دليل للممارسة الموصى بها؛ ولا يحوّل أي منها الميزة غير الموجودة إلى ميزة منفذة.

## كيف تستخدم الحزمة؟

ابدأ من `00-governance/Documentation-Standard.md` لفهم قواعد قراءة هذه الوثائق، ثم اقرأ نظرة المنتج والنطاق، فالمعمارية، ثم Domain وAPI وقاعدة البيانات والأمن والاختبارات والتشغيل. وثائق `09-traceability` تربط السلوك الملحوظ بمواقع الكود والاختبارات. أما `10-open-items` فتسجل ما لم يثبت أو ما يحتاج معالجة قبل إطلاق إنتاجي.

## شجرة الوثائق

```text
RetailFlow_Documentation_v1.0/
├── README.md
├── 00-governance/
│   ├── Documentation-Standard.md
│   └── Repository-Baseline.md
├── 01-product/
│   ├── Product-Overview.md
│   └── Implemented-Capabilities-and-Scope.md
├── 02-architecture/
│   ├── Architecture-Overview.md
│   ├── Runtime-Pipeline-and-Request-Flow.md
│   ├── Architecture-Diagrams.md
│   └── adr/
│       ├── README.md
│       ├── ADR-001-Layered-Product-Host.md
│       ├── ADR-002-SQL-Server-and-EF-Core.md
│       ├── ADR-003-Store-Scoped-Tenancy.md
│       ├── ADR-004-Cookie-Authentication-and-Server-Side-Tickets.md
│       └── ADR-005-Checkout-Idempotency-and-Inventory-Reservation.md
├── 03-domain/
│   ├── Domain-Model.md
│   └── Commerce-Lifecycle.md
├── 04-api/
│   ├── API-Reference.md
│   └── API-Errors-and-Validation.md
├── 05-data/
│   ├── Database-Architecture.md
│   ├── Data-Dictionary.md
│   └── EF-Migrations.md
├── 06-security/
│   ├── Security-Architecture.md
│   └── OWASP-Verification-Matrix.md
├── 07-quality/
│   ├── Testing-Strategy.md
│   └── CI-Pipeline.md
├── 08-operations/
│   ├── Configuration-Reference.md
│   ├── Local-Development.md
│   ├── Deployment-Runbook.md
│   └── Health-and-Operations.md
├── 09-traceability/
│   └── Feature-Code-Test-Matrix.md
├── 10-open-items/
│   └── Production-Gaps-and-Decision-Backlog.md
└── 11-references/
    └── Standards-and-References.md
```

## قواعد الحالة

- **Implemented:** يوجد تنفيذ في Product Host الأساسي `src/`، ويُشار إلى موضع الدليل.
- **Tested:** يوجد اختبار في `tests/` أو CI ينفّذ الاختبار؛ هذا لا يعني أن جميع ظروف الإنتاج قد اختُبرت.
- **Partially implemented:** التنفيذ موجود لكن هناك اعتماد أو سلوك مهم غير مكتمل/غير موصول.
- **Experimental:** الكود موجود تحت `experiments/` ولا يُعامل كجزء من Product Host.
- **Not found:** لم يظهر تنفيذ في الشجرة المفحوصة. لا يعني ذلك استحالة وجوده خارج هذا المستودع.
- **Open decision:** لا يكفي الكود لتحديد قرار المنتج أو التشغيل بصورة موثوقة.

## حدود الاعتماد

هذه النسخة Baseline توثيقية أولى. هي ليست Product Requirements Specification معتمدة من أصحاب المصلحة، ولا إثبات امتثال OWASP، ولا تقرير Penetration Test، ولا إثبات جاهزية Production. يجب مراجعة البنود المفتوحة في `10-open-items` وإقرارها قبل تحويل الوثائق إلى Baseline معتمد.
