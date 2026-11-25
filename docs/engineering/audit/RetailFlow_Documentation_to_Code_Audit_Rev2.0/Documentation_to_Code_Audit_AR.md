# RETAILFLOW
## تقرير التدقيق الشامل للوثائق مقابل الكود

**Documentation-to-Code Audit Report**  
**الإصدار:** 1.0  
**التاريخ:** 25 نوفمبر 2026  
**الوثائق محل التدقيق:** RetailFlow Engineering Documentation Revision 2.0.0  
**Git baseline:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`  
**الفرع:** `main`  
**القرار:** `NOT APPROVED AS FINAL ENGINEERING BASELINE` — صالح كمسودة هندسية بعد معالجة الملاحظات المحددة.

---

# 1. الملخص التنفيذي

أُجري هذا التدقيق للتحقق من اتساق وثائق Revision 2.0 مع الملفات التأسيسية السبعة، وشجرة مستودع RetailFlow عند الـ commit المرجعي، ومسارات التنفيذ والاختبارات وCI. لم يكن هدف التدقيق تقييم جمال المستندات أو عدد صفحاتها، بل التحقق من أن الادعاءات والمتطلبات والقرارات والمخططات يمكن تتبعها إلى مصدر أو كود أو اختبار أو دليل اعتماد.

**النتيجة:** Revision 2.0 تمثل دليلاً هندسياً جيد التنظيم وواسع التغطية، وتفصل بشكل صحيح بين `Implemented` و`PoC` و`Proposed` و`Open Decision` في مواضع مهمة. لكنها لا تزال **Engineering Baseline Draft** وليست Baseline نهائياً قابلاً للاعتماد؛ لأن بعض السجلات متعارضة، وربط المتطلبات بالأدلة غير مكتمل، وبعض المخططات المستهدفة تحمل تناقضات تصميمية يجب حلها قبل تنفيذ Checkout/Orders، كما أن عدة فصول A-to-Z ما زالت أوصاف نطاق لا مواصفات تنفيذ قابلة للبناء والاختبار.

## 1.1 نتيجة التدقيق

| التصنيف | العدد | التفسير |
|---|---:|---|
| High | 5 | تعارض أو فجوة تمنع اعتماد خط الأساس أو قد تنتج تنفيذاً غير صحيح |
| Medium | 8 | نقص مهم في التتبع أو النمذجة أو حوكمة الأدلة أو معايير الجودة |
| Low | 2 | تحسينات توافق/تشغيل لا تمنع قبول الـ current slice |
| Critical | 0 | لم يثبت وجود عيب Critical في النطاق الذي أمكن فحصه |

لا تعني هذه الأعداد أن الكود خالٍ من العيوب؛ إنها تصنيف للملاحظات التي أمكن إثباتها من المصادر المتاحة.

## 1.2 القرار المطلوب

1. إبقاء Revision 2.0 بحالة `Draft` وعدم إعلان اكتمال التوثيق أو Production Readiness.
2. معالجة جميع ملاحظات High قبل اعتماد Architecture/Requirements Baseline.
3. معالجة Medium الخاصة بالـ traceability والمخططات ومصفوفة الأمن قبل بدء وحدات Cart/Checkout/Orders.
4. تحويل كل قرار مفتوح إلى سجل مالك/موعد/اعتماد، وعدم تحويل `Proposed` إلى `Accepted` دون سجل قرار ودليل.
5. بعد الإصلاح، تحديث Markdown والفصول المنفصلة والسجلات والمخططات وPDF/Word من مصدر واحد في نفس Pull Request.

---

# 2. نطاق التدقيق ومنهجيته

## 2.1 المصادر التي تمت مراجعتها

- الملفات الأصلية: `01` إلى `07`، مع اعتماد Phase 06 Revision 1.2 مرجعاً أحدث من النسخة السابقة.
- ملفات Revision 2.0: الدليل الرئيسي Markdown/Word/PDF، الفصول الـ22، السجلات السبعة، المخططات الثمانية، OpenAPI 3.1.1، وملفات README وBaseline Evidence.
- مستودع `ayhm325/RetailFlow` عند commit `04392c875c275e99f4b5ee378f63b3a8b8fc541d`.
- Product Host: `Program.cs`, `ProductHostExtensions`, Catalog Endpoints/Filter, Application Contracts/Context, Domain entities, EF configurations, migrations, Infrastructure services.
- الاختبارات: `PublicStoreCatalogIntegrationTests.cs` و`ProductHostIntegrationTests.cs`، وملفات اختبارات PoC ذات الصلة.
- التشغيل: `.github/workflows/ci.yml`، GitHub Actions Run #10، وCheck Run المرتبط به.
- ملفات التشغيل والتوثيق داخل المستودع: `AUTH-ADR-01-Session-Transport.md`, `phase-06-identity-spike-status.md`, `product-host-bootstrap.md`, `public-store-catalog-slice.md`.

## 2.2 حدود المنهجية

- شُغلت مراجعة شجرة GitHub كاملة: 232 مدخلاً، منها 166 ملفاً و66 مجلداً. شملت الشجرة 132 ملف C# و13 ملف مشروع `.csproj` وأربعة ملفات `.slnx`.
- فُحصت ملفات التنفيذ الحالية والمسارات الأساسية والـ migrations والـ tests والـ workflow ذات الصلة بالـ current slice، كما فُحصت ملفات PoC الرئيسية للتأكد من أنها منفصلة عن Product Host.
- لم يُجرِ هذا التدقيق قراءة سطرية مستقلة لكل سطر من جميع ملفات C# الـ132؛ لذلك لا يمثل Full Code Security Audit أو مراجعة كود كاملة.
- لم يتم تشغيل `dotnet build` أو `dotnet test` محلياً في بيئة هذا التدقيق. تم التحقق من GitHub Actions Check Run الناجح على نفس SHA، وهو دليل CI تاريخي على ذلك الـ commit.
- لم يتم نشر التطبيق إلى Azure، أو تشغيل Playwright من جديد، أو إجراء Penetration Test، أو Load Test، أو Restore Drill.
- تم تحليل OpenAPI YAML نحوياً بنجاح باستخدام YAML parser ومقارنة المسارات المعلنة بالمسارات الثلاثة الموجودة في المصدر؛ لم تتوفر حزمة OpenAPI semantic validator في بيئة التدقيق، لذلك لم يُدّعَ إجراء تحقق رسمي كامل للمخطط.

## 2.3 تعريف درجات الخطورة

- **High:** يتطلب معالجة قبل اعتماد Baseline أو قبل تنفيذ مسار تجاري يعتمد عليه.
- **Medium:** يلزم معالجته قبل بوابة المرحلة المرتبطة، أو قبل إعلان توثيق/قبول أمني وتشغيلي مكتمل.
- **Low:** تحسين واضح أو قرار توافق/حوكمة لا يثبت وحده وجود خلل إنتاجي.

---

# 3. تثبيت Baseline التنفيذ الفعلي

## 3.1 حالة Git والـ CI

- `main` كان يشير إلى `04392c875c275e99f4b5ee378f63b3a8b8fc541d` وقت الفحص.
- GitHub Actions: Workflow `RetailFlow CI`, Run #10، Run ID `45623148657`، الحالة `completed`, النتيجة `success`.
- Check Run: `Build, SQL Integration & Playwright E2E`, النتيجة `success`، بدأ في `2026-11-25T13:23:53Z` واكتمل في `2026-11-25T13:26:52Z`.
- هذا يثبت نجاح الـ workflow المحدد عند ذلك الـ commit، ولا يثبت النشر أو الجاهزية الإنتاجية أو تنفيذ ميزات غير موجودة.

رابط التشغيل: https://github.com/ayhm325/RetailFlow/actions/runs/45623148657

## 3.2 ما هو موجود فعلاً في Product Host

**Implemented في هذا snapshot:**

- حلول ومشاريع `RetailFlow.Api`, `RetailFlow.Application`, `RetailFlow.Domain`, `RetailFlow.Infrastructure`.
- SQL Server + EF Core و`RetailFlowDbContext` وmigrations الخاصة بـ Catalog Slice.
- Store resolution عبر `{slug}` إلى Store نشط من قاعدة البيانات ووضع `StoreRequestContext` في Scope الطلب.
- ثلاث واجهات Public Read فقط:
  - `GET /api/stores/{slug}/catalog/categories`
  - `GET /api/stores/{slug}/catalog/products`
  - `GET /api/stores/{slug}/catalog/products/{productId:guid}`
- Public catalog filters للمنتج المنشور والتصنيف النشط والمتجر المقصود، مع pagination/sort/search و`AsNoTracking()` projections.
- قيود SQL مهمة في الـ current catalog schema: مفاتيح مركبة مرتبطة بـ `StoreId`، وSKU فريد داخل المتجر، و`RowVersion` للمخزون، وقيود `OnHand`/`Reserved`، وقيود العملة ودقة الأسعار.
- Health endpoints، ProblemDetails/exception handling، Host filtering، HTTPS/HSTS، وSPA fallback مشروط بوجود `wwwroot/index.html`.

## 3.3 ما هو PoC وليس تكاملاً في Product Host

- `experiments/IdentitySpike` يثبت Identity scoping وCustom UserStore في مشروع مستقل.
- `experiments/SessionTransportSpike` يثبت Cookie/session/CSRF/ITicketStore/Data Protection في مشروع مستقل.
- لا توجد references من Product Host إلى مشروعي التجارب عند هذا الـ commit، ولا `UseAuthentication()` أو `UseAuthorization()` في Product Host.
- نجاح PoC لا يساوي دمجاً في المنتج أو اعتماداً نهائياً لقرار المصادقة.

## 3.4 غير موجود في Product Host عند Baseline

لا يوجد React `ClientApp`، ولا Merchant Catalog write APIs، ولا Customer Profile/Address APIs، ولا Cart، ولا Checkout، ولا Orders، ولا Inventory Reservation lifecycle، ولا Delivery Zones، ولا Payment integration، ولا Production Azure Deployment workflow.

هذه ليست عيوباً في الـ current slice إذا بقيت موسومة Target/Not Implemented؛ تصبح عيب توثيق عندما تُعرض كمنجز أو عندما يُعتمد تصميمها من دون مواصفاتها التفصيلية.

---

# 4. سجل النتائج التفصيلي

## AUD-001 — تعارض Acceptance Criteria بين الفصل والسجل

**الخطورة:** High  
**الموقع:** `docs/02-chapter-02.md` مقابل `registers/acceptance-criteria.csv`.

**الدليل:** الفصل 02 يعرض AC-01 إلى AC-07. أما CSV فيعرض AC-01 إلى AC-10. والأهم أن AC-07 في الفصل يختص بالتشغيل المحلي والنشر التجريبي وتوثيق API والاختبارات، بينما AC-07 في CSV يختص باستعادة Backup والتحقق من Schema وبيانات المجال ومفاتيح التشفير. كما أن AC-08 إلى AC-10 موجودة في CSV فقط.

**الأثر:** يوجد مصدران متعارضان لمعايير قبول النسخة الأولى؛ وقد تربط فرق التنفيذ والاختبار نفس AC بمعنيين مختلفين.

**الإصلاح المطلوب:** اعتماد `acceptance-criteria.csv` كمصدر وحيد للمعايير، أو اعتماد الفصل كمصدر وحيد؛ ثم توحيد IDs والتعريفات. إن كانت AC-07 القديمة مهمة، تنقل إلى ID جديد غير مستخدم. يجب أن يشير الفصل إلى السجل بدلاً من إعادة تعريف معايير القبول بنسخة ثانية.

**بوابة الإغلاق:** لا يوجد AC ID مكرر أو مختلف المعنى؛ كل AC له Owner وPriority وTest ID وEvidence Location وStatus.

## AUD-002 — Requirements Traceability غير قابلة للتنفيذ بعد

**الخطورة:** High  
**الموقع:** `registers/requirements-traceability.csv`.

**الدليل:** السجل يحوي 42 متطلباً FR، لكنه يستخدم نفس النص العام في حقل `Acceptance Evidence` لكل الصفوف: “راجع AC المقابل… أضف test ID قبل الإغلاق”. لا يحتوي السجل على `AC ID` محدد، أو `BR ID`، أو `NFR ID`، أو Test Case ID، أو مسار Code Evidence، أو رابط CI Run. لذلك لا يمكن تتبع متطلب معين إلى اختبار معين أو دليل معين دون تخمين.

**الأثر:** وجود سجل Traceability شكلي، وليس RTM تشغيلية. لا يمكن إثبات اكتمال Requirement أو تحليل أثر تغييره بصورة موثوقة.

**الإصلاح المطلوب:** إضافة حقول مستقلة: `FR`, `UC`, `BR`, `NFR`, `API/Component`, `AC ID`, `Test ID`, `Source Path`, `Evidence Commit`, `CI Run`, `Status`, `Deviation/Exception`. لكل FR يجب وجود اختبار إيجابي وسلبي حيث ينطبق، أو Deviation معتمد.

**بوابة الإغلاق:** جميع FR-001..FR-042 لها روابط مكتملة أو استثناء موثق؛ لا توجد قيمة Evidence عامة غير محددة.

## AUD-003 — قواعد الأعمال الأصلية BR-001..BR-012 غير مرتبطة بالـ Invariants والاختبارات

**الخطورة:** High  
**الموقع:** Phase 02 الأصلية، Phase 04 الأصلية، `docs/04-chapter-04.md`، وسجلات Rev2.0.

**الدليل:** الوثيقة الأصلية Phase 02 تعرف BR-001 إلى BR-012. الفصل 04 الجديد يصنف قواعد جديدة مثل `ST-*`, `PR-*`, `IN-*`, `OR-*`، لكنه لا يقدم Mapping صريحاً من كل BR قديم إلى invariant الجديد أو Aggregate أو كود/اختبار. لا يوجد BR column في Traceability CSV.

**الأثر:** قد تُفقد قاعدة أعمال أصلية أثناء إعادة تسمية النموذج؛ ولا يظهر ما إذا كانت قاعدة معينة حُفظت أو تغير معناها أو أُجلت.

**الإصلاح المطلوب:** إنشاء Business Rule Traceability Matrix: `BR ID -> New invariant ID -> Domain owner/Aggregate -> Persistence constraint -> Test ID -> Evidence`. لا تحذف BR قديمة؛ سجّل `supersedes` أو `changed by` مع rationale إذا تغيرت.

**بوابة الإغلاق:** BR-001..BR-012 كلها مغطاة أو موقوفة بقرار معتمد ومبرر.

## AUD-004 — مخطط Order State يخلط ثلاث حالات مستقلة

**الخطورة:** High  
**الموقع:** `diagrams/order-state-target.mmd` مقابل `docs/04-chapter-04.md`.

**الدليل:** الفصل 04 يذكر صراحة أن `OrderStatus` و`FulfillmentStatus` و`PaymentStatus` حالات منفصلة. لكن مخطط الحالة يضع `PendingConfirmation`, `Processing`, `Packed`, `Shipped`, `ReadyForPickup`, `Delivered`, `PickedUp`, `Completed` في State Machine واحدة، ويمزج تقدم الطلب مع طريقة التنفيذ والتسليم وسياسة الدفع/الإغلاق.

**الأثر:** عند التنفيذ قد تصبح الانتقالات غير واضحة: هل تعني `Completed` اكتمال التجهيز أم التسليم أم التحصيل؟ كيف يتصرف COD؟ هل يمكن أن يصبح الطلب Delivered بينما PaymentStatus ما زال Pending؟ ومنع الإلغاء على أي محور؟

**الإصلاح المطلوب:** فصل ثلاثة State Models:

1. `OrderStatus`: PendingConfirmation / Confirmed / Rejected / Cancelled / Completed وفق سياسة واضحة.
2. `FulfillmentStatus`: NotStarted / Processing / Packed / OutForDelivery / ReadyForPickup / Delivered / PickedUp.
3. `PaymentStatus`: Pending / Collected / Failed / Refunded أو الحالات التي يعتمدها Product Owner.

أضف Policy Rules تصف التفاعل بين المحاور، مثل متى يمكن اعتبار COD Collected، ومتى يسمح بإغلاق Order، ومن يملك كل انتقال.

**بوابة الإغلاق:** لكل محور جدول انتقالات مستقل، مع tests لكل انتقال صحيح ومرفوض وحالة متزامنة.

## AUD-005 — Checkout Idempotency diagram لا يوضح Atomic Claim

**الخطورة:** High  
**الموقع:** `diagrams/checkout-sequence-target.mmd`، `docs/04-chapter-04.md`، `docs/13-checkout.md`.

**الدليل:** المخطط يفحص نتيجة Idempotency قبل بدء Transaction. ثم في فرع New Checkout يبدأ Transaction ويضيف الطلب والنتيجة. لا يوضح المخطط حجز مفتاح Idempotency ذرياً داخل المعاملة، أو Unique Constraint على `(StoreId, CustomerId, IdempotencyKey)`، أو مسار معالجة تعارض المفتاح عند طلبين متزامنين. وجود Lookup أولي وحده لا يمنع طلبين متزامنين من رؤية “لا توجد نتيجة” والبدء معاً.

**الأثر:** خطر إنشاء طلبين عند إعادة إرسال Checkout بنفس المفتاح تحت التزامن، أو إرجاع نتائج مختلفة إذا حدثت منافسة على المفتاح.

**الإصلاح المطلوب:** وثّق Atomic Idempotency Claim: إنشاء سجل المفتاح تحت Unique Constraint داخل Transaction أو آلية قفل/Claim مكافئة؛ ثم إعادة قراءة النتيجة الموجودة عند Unique Conflict؛ وربط الطلب/النتيجة بالمفتاح في المعاملة ذاتها. عرّف سلوك الطلب الجاري والفشل وإعادة المحاولة ومدة الاحتفاظ بالمفتاح.

**بوابة الإغلاق:** SQL Server concurrency tests لطلبين متزامنين بنفس المفتاح وبمفتاحين مختلفين؛ إثبات Order واحد فقط للمفتاح نفسه، وعدم بقاء Reservation أو Order جزئي بعد rollback.

## AUD-006 — غياب ERD وData Dictionary تفصيليين في التوثيق الجديد

**الخطورة:** Medium  
**الموقع:** `docs/05-chapter-05.md`، `diagrams/`، مقارنةً بـ Phase 05 الأصلية.

**الدليل:** الفصل 05 يلخص الكيانات والقيود والقرارات، لكنه لا يقدم ERD موحداً بعلاقات/Cardinalities واضحة ولا Data Dictionary كاملاً يحدد لكل جدول/عمود: SQL type، Length/Precision، Nullability، Default، PK/FK/Unique/Check، Index، Delete Behavior، ومصدر القيمة. مخطط Component الحالي ليس ERD. كما أن Phase 05 الأصلية ذات 24 صفحة أوسع من الفصل الجديد الذي يختصرها إلى ملخص قصير.

**الأثر:** يصعب مراجعة اكتمال النموذج والقيود والـ migration قبل بدء Customers/Cart/Orders/Reservations، وقد ينشأ تضارب بين Domain Model وEF mapping وSQL schema.

**الإصلاح المطلوب:** فصل `Current Schema ERD` المثبت من EF Migrations عن `Target Commerce ERD` المقترح. إضافة Data Dictionary وRelationship Catalogue وIndex Rationale وMigration/Rollback Plan لكل مجموعة جداول.

**بوابة الإغلاق:** المخطط الحالي مطابق للـ EF Model/SQL Migration؛ المخطط المستهدف موسوم Proposed؛ لكل علاقة Tenant-owned قيد يثبت توافق StoreId؛ ولا يوجد حقل أو FK بلا تعريف.

## AUD-007 — Use Case Catalogue في Rev2.0 ملخص، وليس بديلاً كاملاً عن Phase 03

**الخطورة:** Medium  
**الموقع:** `docs/03-chapter-03.md` مقارنةً بـ `03_RetailFlow_Use_Case_Catalogue_AR(8).pdf`.

**الدليل:** الفصل الجديد يسرد UC-01..UC-10 ويعطي لكل حالة فقرة موجزة. لكنه لا يحتفظ بعمق كتالوج الاستخدام الأصلي لكل حالة: Preconditions، Trigger، Main Success Flow مرقم، Alternate Flows، Exceptions، Postconditions، Business Rules المرتبطة، وAcceptance Tests. كما لا توجد خريطة تفصيلية لكل خطوة إلى API/Domain/DB.

**الأثر:** الوصف الحالي جيد للقراءة لكنه غير كافٍ وحده كعقد تنفيذ واختبار لفريق متعدد الأدوار.

**الإصلاح المطلوب:** احتفظ بملخص UC في الدليل الرئيسي، وأنشئ مواصفة تفصيلية منفصلة لكل UC تجاري مع Preconditions/Trigger/Actors/Normal Flow/Alternate & Exception Flow/Postconditions/BR/FR/AC/Test IDs.

**بوابة الإغلاق:** كل FR في نطاق MVP يرتبط بواحد أو أكثر من UC مفصل؛ ولا توجد خطوة شراء أو إلغاء أو موافقة بدون استثناءات ومعايير قبول.

## AUD-008 — مخططات C4 الحالية ليست C4 صحيحة بالكامل

**الخطورة:** Medium  
**الموقع:** `diagrams/c4-context-current.mmd` و`diagrams/c4-container-target.mmd`.

**الدليل:** مخطط Context يضع `Public Catalog Read API` كأنه نظام خارجي يتصل به RetailFlow، مع أنه قدرة/Endpoint داخل النظام. كما يرسم SQL Server خارج RetailFlow مباشرة من دون توضيح أنه Container/Database داخل حدود تشغيل النظام. مخطط Container مرسوم كـ Flowchart بطبقات Browser/API/Application/Domain/Infrastructure؛ هذا مفيد كتصور، لكنه ليس وصف C4 Container/Component مكتمل العناصر والعلاقات، ولا يحدد مسؤولية/تقنية/حدود كل Container بدقة.

**الأثر:** قد يقرأ مهندس المخطط على أنه C4 Context/Container رسمي، فيخلط حدود النظام مع مكوناته الداخلية أو مزودي الخدمة الخارجيين.

**الإصلاح المطلوب:** Context: الأشخاص والأنظمة الخارجية حول `RetailFlow` فقط. Container: React SPA، Product Host، SQL Database، Object Storage، Telemetry/External Providers عندما تصبح معتمدة. Component: مكونات داخل Product Host. ميّز Current وTarget، ولا تضع عنصر داخلي كـ External System.

**بوابة الإغلاق:** أسماء المخططات ومستواها متوافقة مع C4؛ كل عنصر له وصف ومسؤولية وتقنية؛ العلاقات موسومة ببروتوكول/غرض.

## AUD-009 — Security Control Matrix لا يحقق ASVS traceability بعد

**الخطورة:** Medium  
**الموقع:** `registers/security-control-matrix.csv` و`docs/16-security-privacy-threat-model.md`.

**الدليل:** المصفوفة تتضمن 16 ضابطاً جيداً على مستوى المجال، لكنها لا تحمل معرفات متطلبات OWASP ASVS 5.0.0، أو مستوى التحقق المستهدف، أو Applicability/Exclusion Rationale، أو Test ID، أو Evidence URI، أو Status يثبت التطبيق. الفصل 16 يوضح أن هذه Matrix أولية ولا تعني الامتثال الكامل؛ وهذا توضيح صحيح لكنه يعني أن بوابة الأمن غير مكتملة.

**الأثر:** لا يمكن الادعاء بتغطية ASVS أو قياس نسبة الضوابط المتحققة.

**الإصلاح المطلوب:** Mapping لكل ضابط: `ASVS Requirement ID -> Applicable? -> Product Component -> Implementation -> Test -> Evidence -> Exception/Risk Owner`. حدّد مستوى التحقق المناسب بعد Threat Modeling، ولا تستخدم رقم إصدار المعيار دون معرفات متطلباته.

**بوابة الإغلاق:** جميع المتطلبات المنطبقة لها Evidence؛ جميع الاستثناءات لها rationale ومالك وموافقة؛ لا توجد ضوابط Critical غير مختبرة.

## AUD-010 — معايير NFR التشغيلية غير قابلة للقياس بعد

**الخطورة:** Medium  
**الموقع:** `docs/02-chapter-02.md` و`docs/18-sre-operations.md` و`registers/open-decisions.csv`.

**الدليل:** الوثائق تشير إلى P95، عدد المتاجر، الحمل، Availability، RPO/RTO، Retention، وميزانية التشغيل كقيم لم تُحسم بعد. وجود وسائل تحقق عامة مثل Load Test أو Restore Drill لا يحدد نتيجة قبول رقمية.

**الأثر:** لا يمكن اتخاذ قرار Performance/Capacity أو قبول Staging/Production موضوعياً قبل تحديد أهداف المنتج.

**الإصلاح المطلوب:** اعتماد Quality Attribute Scenarios رقمية: عدد المتاجر/المستخدمين/الطلبات المتزامنة، P95/P99 لكل endpoint مهم، Error Rate، Availability target، DB/Storage budget، RPO/RTO، Backup frequency/retention، واستعادة مفاتيح Data Protection. اجعل القيم قراراً مالكاً لا تقديراً صامتاً من المهندس.

**بوابة الإغلاق:** Product Owner وEngineering/SRE يعتمدون الأهداف؛ وتوجد قياسات Staging وRestore evidence مقابلها.

## AUD-011 — Roadmap يضع الـ Current Product Host Slice بحالة Target

**الخطورة:** Medium  
**الموقع:** `registers/phase-roadmap.csv`، الصف G.

**الدليل:** الصف G اسمه `Product Host Foundation` وWorkstream=`Current slice`، لكن `Baseline Status` يساوي `Target / status must be updated by evidence`، مع أن Product Host وPublic Catalog Read API وSQL migrations وIntegration Tests موجودة في المستودع عند الـ commit المرجعي.

**الأثر:** خارطة الطريق لا تميز الإنجاز الحالي عن العمل المستقبلي، وتفقد قيمتها كمرجع تقدم.

**الإصلاح المطلوب:** اجعل المرحلة G `Implemented - current slice` أو `Partially Implemented` مع روابط الملفات والـ CI evidence، وافصل ما لم يُنفذ مثل Identity Integration أو Merchant Write APIs في مراحل K وما بعدها. احتفظ بحالة Target للمخرجات المستقبلية فقط.

**بوابة الإغلاق:** كل Phase لها status مدعوم بدليل وCommit وTest/Acceptance reference.

## AUD-012 — Baseline Evidence يعرض حالة CI بصورة قديمة/مضللة

**الخطورة:** Medium  
**الموقع:** `evidence/baseline-evidence.md`.

**الدليل:** الملف يقول إن GitHub commit status هو `pending` ولا توجد status entries. لكن فحص Actions API وCheck Runs على نفس SHA أظهر Workflow Run #10 بحالة `completed/success`، وCheck Run `Build, SQL Integration & Playwright E2E` بحالة `completed/success`.

**التفسير:** من المحتمل أن الملف فحص Statuses API التقليدي فقط، ولم يضم Check Runs/Actions. عبارة `pending` لا تمثل النتيجة الكاملة.

**الأثر:** سجل الأدلة يناقض دليل CI الفعلي وقد يضلل قبول المرحلة.

**الإصلاح المطلوب:** سجل المصدرين منفصلين: `Commit Status API` (لا توجد Status contexts) و`GitHub Actions Check Run` (نجاح). احفظ Run ID والرابط والتاريخ والـ SHA. لا تستنتج فشل أو Pending من أحد المصدرين منفرداً.

**بوابة الإغلاق:** Evidence file يعرض النتيجة الصحيحة ويظل قابلاً لإعادة التحقق.

## AUD-013 — الفصل 21 المنفصل لا يطابق نسخة الفصل في الدليل الرئيسي

**الخطورة:** Medium  
**الموقع:** `docs/21-chapter-21.md` مقابل `Master_Engineering_Handbook_AR.md`.

**الدليل:** النسخة الرئيسية تحتوي فقرة `ملاحظات حالة المراجع عند البحث - 25 نوفمبر 2026` مع تحديثات محددة عن ISO/IEC/IEEE 29148 و42010 وASVS وarc42 وC4 وAzure. هذه الفقرة غير موجودة في `docs/21-chapter-21.md`. بقية الفصل متطابقة تقريباً، لكن هذا الجزء يثبت وجود مصدرين غير متزامنين.

**الأثر:** قد يتغير الدليل الرئيسي دون الفصول Docs-as-Code أو العكس، فتظهر نسختان مختلفتان للمعلومة ذاتها.

**الإصلاح المطلوب:** اجعل الفصول المنفصلة هي Source of Truth وولّد Master منها آلياً، أو العكس. أضف CI job يقارن تجميع الفصول أو يولّد PDF/Word من نفس الملفات ويمنع الدمج عند اختلاف النسخ.

**بوابة الإغلاق:** Build pipeline يعيد إنتاج Master من Docs sources؛ لا توجد اختلافات غير مقصودة بين الفصل المنفصل والرئيسي.

## AUD-014 — OpenAPI 3.1.1 يحتاج قرار توافق موثقاً وتغطية أخطاء أكمل

**الخطورة:** Low  
**الموقع:** `api/openapi-current.yaml`.

**الدليل:** الملف Draft ويعلن المسارات الثلاثة الحالية فقط، وهذا متوافق مع نطاق الكود. لكنه يستخدم OpenAPI 3.1.1 رغم أن الموقع الرسمي يعرض 3.2.1 كإصدار منشور بتاريخ 10 سبتمبر 2026. لا يوجد سبب توافق مسجل لاختيار 3.1.1، كما لا تظهر استجابات 500 العامة في العمليات، وبعض common headers/response metadata غير موثقة.

**الأثر:** قد يتأخر العقد عن إصدار المواصفة أو لا يصف كل المخرجات المتوقعة. استخدام 3.1.1 ليس خطأ تلقائياً؛ قد يكون اختياراً صحيحاً إذا فرضته أدوات التوليد أو العملاء.

**الإصلاح المطلوب:** سجّل قراراً بين 3.1.1 و3.2.1 حسب توليد OpenAPI في .NET 10 ومستهلكي TypeScript؛ تحقق من generated document وtoolchain. أضف 500/ProblemDetails المشتركة حيث تنطبق، وسياسات caching/headers إن اعتمدت.

**بوابة الإغلاق:** OpenAPI validator + diff مع generated Product Host document + client generation compatibility test.

## AUD-015 — كلمة مرور SQL الخاصة بخدمة CI ثابتة داخل ملف workflow

**الخطورة:** Low  
**الموقع:** `.github/workflows/ci.yml`.

**الدليل:** قيمة `MSSQL_SA_PASSWORD` وكلمة مرور الاتصال بقاعدة SQL Server الخاصة بخدمة CI مكتوبة كنص صريح في YAML. السياق الحالي يظهر أنها تستخدم حاوية SQL Server مؤقتة داخل GitHub-hosted runner، ولا يوجد دليل أنها كلمة مرور Production.

**الأثر:** ليست دليلاً على تسرب credential إنتاجي؛ لكنها تضع كلمة مرور ثابتة قابلة لإعادة الاستخدام في مستودع عام، وقد تُنسخ لاحقاً إلى بيئات أخرى بالخطأ.

**الإصلاح المطلوب:** ولّد كلمة مرور عشوائية لكل Run عبر `GITHUB_ENV`/step output، أو استخدم قيمة واضحة أنها disposable test-only مع تدويرها وعدم إعادة استخدامها. استمر في عزلها عن أي Secret/Environment إنتاجي.

**بوابة الإغلاق:** لا توجد قيمة ثابتة قابلة لإعادة الاستخدام في Workflow، ولا يتم تمريرها إلى artifacts/logs.

---

# 5. مراجعة المخططات الثمانية

| المخطط | التقييم | النتيجة |
|---|---|---|
| `c4-context-current.mmd` | Needs correction | يضع Public Catalog API كأنه نظام خارجي ويخلط بعض حدود النظام/التخزين |
| `c4-container-target.mmd` | Needs refinement | مفيد كـ high-level flow، لكنه لا يوضح Containers C4 بعقودها وحدودها بدقة |
| `component-current.mmd` | Partial | يصف مسار Catalog الحالي، لكنه لا يعرض Host middleware/DI/health أو كل علاقات الطلب |
| `request-pipeline-target.mmd` | Target - review | مفيد كمقترح، وليس وصفاً للـ Product Host الحالي؛ يحتاج إثباتاً عند دمج Identity/CSRF |
| `checkout-sequence-target.mmd` | High issue | ترتيب idempotency lookup قبل Transaction دون Atomic Claim أو unique-conflict flow |
| `order-state-target.mmd` | High issue | يجمع Order/Fulfillment/Payment lifecycles رغم أن النص يفصلها |
| `deployment-target.mmd` | Target - acceptable | يوضح عناصر غير مختارة بعد؛ يجب أن يبقى موسوماً Target |
| `ci-cd-target.mmd` | Target - acceptable | يمثل Pipeline مستقبلياً؛ لا يطابق الـ CI workflow الحالي بعد، ويجب عدم وصفه كتنفيذ قائم |

---

# 6. مراجعة API وDatabase مقابل الكود

## 6.1 API

- مسارات OpenAPI الثلاثة تطابق المسارات الموجودة في `PublicCatalogEndpoints.cs`.
- Store slug يمر عبر `StoreResolutionEndpointFilter`، ويُحل إلى متجر نشط على الخادم.
- مسار المنتجات يطبق حدود `page >= 1`, `1 <= pageSize <= 50`, `search <= 100`, وخيارات sort المعروفة؛ ويوجد اختبارات ValidationProblemDetails.
- Public query يفرض `StoreId` و`Published` و`Category.IsActive`، مع ترتيب deterministic وpagination.
- لا يوجد حالياً API مصادقة أو Merchant mutation ضمن Product Host؛ لذلك لا يجوز أن توحي API docs بوجودها.

## 6.2 Database / Persistence

المخطط الحالي يثبت دفاعات جيدة في Catalog Slice:

- `Products` مرتبط بـ `Categories` باستخدام `(StoreId, CategoryId)`.
- `ProductImages` مرتبط بـ `Products` باستخدام `(StoreId, ProductId)`.
- `InventoryItems` مرتبط بـ `Products` باستخدام `(StoreId, ProductId)` وبقيد فريد للمنتج داخل المتجر.
- SKU فريد لكل متجر عندما لا يكون NULL.
- `InventoryItem.RowVersion` مهيأ كـ SQL Server rowversion، وقيود SQL تمنع `OnHand < 0`, `Reserved < 0`, و`Reserved > OnHand`.
- قواعد العمل تمنع دقة سعر غير متوافقة مع `Currency.MinorUnit`، وتوجد قيود على العملة وStoreSettings.

هذه الضوابط تثبت قاعدة بيانات Catalog الحالية، لكنها لا تثبت Reservation workflow أو منع Oversell في Checkout؛ فهذه العمليات غير موجودة في Product Host عند الـ baseline.

## 6.3 الاختبارات

توجد تغطية تكاملية مهمة لمسار Catalog/Host، بما فيها Store isolation عبر SQL/API، Active/Inactive store، Published/Archived/Draft، Category state، Search/Sort/Pagination، Currency/Precision، Inventory availability projection، Host filtering، ProblemDetails، Health، وSPA fallback.

لكن هذه الاختبارات لا تثبت Cart/Checkout/Orders/Reservation/Payment/React E2E لأنها غير مطبقة في هذا snapshot. كما أن Test Case IDs غير مربوطة بصفوف FR بصورة مباشرة.

---

# 7. ما الذي نجح وما يجب الحفاظ عليه

- فصل `Product Host` عن `IdentitySpike` و`SessionTransportSpike` يحافظ على حدود المنتج ولا يربطه بمشاريع التجارب.
- عدم إعلان PoC كإنجاز Production قرار صحيح ويجب الحفاظ عليه.
- Composite Tenant Foreign Keys في Catalog الحالي توفر حماية على مستوى قاعدة البيانات بالإضافة إلى StoreId query scoping.
- `StoreRequestContextAccessor` Scoped ويمنع ضبط السياق مرتين في نفس Scope.
- `PublicCatalogQueryService` يستخدم projections و`AsNoTracking()` ويطبق شروط النشر والتصنيف والمتجر.
- قيود العملة ودقة الأسعار وSQL CHECK constraints تمنع عدداً من أخطاء البيانات قبل إدخال عمليات البيع.
- الاختبارات الحالية تتجاوز Happy Path وتغطي أخطاء إدخال وعزل وتزامن طلبات قراءة لمتاجر مختلفة.
- GitHub Actions Run #10 ناجح على نفس SHA، ويشغل Build وSQL Integration وPoC وPlaywright بحسب workflow.

---

# 8. خطة الإصلاح قبل اعتماد Engineering Baseline

## Gate 1 — Requirements & Business Baseline

- حل AUD-001 وAUD-002 وAUD-003.
- توحيد AC/BR/FR/UC IDs وملكية كل عنصر.
- إكمال UC specifications من Phase 03 بدلاً من الاكتفاء بالملخص.
- تحديد متطلبات NFR الرقمية التي يحتاجها MVP؛ لا تخترع أهدافاً قبل اعتماد Product Owner.

**مخرج البوابة:** Approved Requirements Baseline + Complete RTM + Business Rule Mapping.

## Gate 2 — Architecture & Data Baseline

- حل AUD-004 وAUD-005 وAUD-006 وAUD-008.
- إعادة رسم Lifecycle diagrams وAtomic Checkout Sequence.
- إكمال Current ERD وTarget ERD وData Dictionary.
- توثيق ADRs الحاسمة بContext/Options/Decision/Consequences/Status/Revisit Trigger.

**مخرج البوابة:** Architecture Description + Accepted ADR Set + Database Design Baseline.

## Gate 3 — Security, Quality & Operations Baseline

- حل AUD-009 وAUD-010.
- بناء ASVS mapping حقيقي مع Test/Evidence IDs.
- اعتماد SLO/Performance/Capacity/Recovery/Retention targets.
- إعداد Staging/Restore/Incident evidence قبل إدعاء Production readiness.

**مخرج البوابة:** Security Verification Plan + Quality Attribute Scenarios + Operational Readiness Criteria.

## Gate 4 — Documentation Automation & Evidence Hygiene

- حل AUD-011 وAUD-012 وAUD-013 وAUD-014 وAUD-015.
- توحيد مصدر Markdown وتجميع Master تلقائياً.
- التحقق من OpenAPI مقابل generated contract.
- إضافة pipeline يتحقق من CSV schemas، IDs، cross references، Mermaid rendering، OpenAPI validation، وPDF/Word generation.

**مخرج البوابة:** Reproducible Docs-as-Code Pipeline + Evidence Pack linked to immutable commit.

---

# 9. شروط اعتماد Engineering Baseline النهائي

لا يُعتمد Baseline نهائي إلا عند تحقق التالي:

- لا توجد High findings مفتوحة.
- لكل FR/BR/UC علاقة واضحة باختبار أو استثناء معتمد.
- لكل مخطط حالة `Current` أو `Target` وتطابق نصوصه مع القرار والنموذج.
- ERD/Data Dictionary/EF mappings لا تتعارض.
- قرارات Identity/Session وSame-Origin وCSRF وSession revocation لها حالة واضحة وموافقات وأدلة.
- الـ Product Host behavior مفصول عن PoCs في كل الوثائق.
- CI وSecurity/Performance/Recovery evidence مربوط بالـ commit نفسه.
- لا توجد ادعاءات Production Ready أو ISO/OWASP compliance دون أدلة تحقق مناسبة.

---

# 10. المصادر المرجعية الخارجية

- ISO/IEC/IEEE 29148:2018 — Requirements Engineering (النسخة المنشورة الحالية وقت التدقيق): https://www.iso.org/standard/72089.html
- ISO/IEC/IEEE DIS 29148 Edition 3 — Draft قيد التطوير، لا يُعامل كنسخة منشورة: https://www.iso.org/standard/94091.html
- ISO/IEC/IEEE 42010:2022 — Architecture Description: https://www.iso.org/standard/74393.html
- arc42 — Architecture Documentation Template (12 sections): https://docs.arc42.org/
- C4 Model — https://c4model.com/
- OWASP ASVS 5.0.0 stable release — https://asvs.owasp.org/ وhttps://asvs.owasp.org/downloads/
- OpenAPI Specification v3.2.1 (10 September 2026): https://spec.openapis.org/oas/v3.2.1.html
- Microsoft EF Core Transactions — https://learn.microsoft.com/ef/core/saving/transactions
- Microsoft EF Core Concurrency — https://learn.microsoft.com/ef/core/saving/concurrency
- ASP.NET Core Forwarded Headers — https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer
- NIST Secure Software Development Framework (SSDF) — https://csrc.nist.gov/Projects/ssdf

---

# 11. الخلاصة النهائية

RetailFlow يملك أساساً تنفيذياً جيداً في Public Catalog Slice، وضوابط Tenant Isolation في بيانات Catalog، واختبارات SQL/API، وتجارب منفصلة متقدمة للهوية والجلسات. كما أن CI على الـ commit المرجعي ناجح.

لكن **Revision 2.0 لا ينبغي أن يعتمد كتوثيق هندسي نهائي أو كامل بعد**. أهم ما يمنع ذلك ليس غياب صفحات إضافية، بل غياب التتبع الدقيق إلى الاختبارات، وتعارض AC IDs، وعدم اكتمال BR mapping، وتناقض مخططات Order/Checkout، وغياب ERD/Data Dictionary المتكامل، وغياب قيم NFR التشغيلية المعتمدة.

التوصية النهائية: احتفظ بـ Rev2.0 كـ `Engineering Baseline Draft`، أصلح الملاحظات High أولاً، ثم اعمل Review ثانياً على نسخة محدثة مرتبطة بcommit جديد. لا تغيّر الكود ضمن هذا التدقيق؛ هذه نتيجة توثيقية وتصميمية وليست تصريحاً بتطبيق الإصلاحات على `main`.
