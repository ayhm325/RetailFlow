# 02. هندسة المتطلبات ومواصفات المنتج

## 02.1 مبادئ المتطلبات

نحافظ على معرفات `FR-001..FR-042`, `BR-001..BR-012`, `UC-01..UC-10`. لا يعاد استخدام معرف لمطلب مختلف. يجب ربط كل FR بحالة استخدام ووحدة ومطلب جودة واختبار قبول ودليل تنفيذ.

## 02.2 المتطلبات الوظيفية

| ID | المتطلب | UC | الوحدة | حالة التنفيذ عند baseline |
|---|---|---|---|---|
| FR-001 | تقديم التاجر طلب إنشاء متجر وإدخال بيانات الحساب والمتجر | UC-01 | Stores | Not implemented |
| FR-002 | مراجعة Platform Admin للطلب والموافقة/الرفض مع تسجيل القرار | UC-02 | Platform Ops | Not implemented |
| FR-003 | Store Slug فريد يستخدم في رابط المتجر | UC-01/03/06 | Stores/Routing | Implemented at data/resolution level |
| FR-004 | تعديل اسم المتجر والشعار والألوان وبيانات التواصل | UC-03 | Stores/Branding | Partial model; write APIs absent |
| FR-005 | دعم Physical وOnlineOnly | UC-01/03 | Stores | Implemented in domain/data model |
| FR-006 | Public address اختياري منفصل عن Fulfillment address | UC-03/09 | Stores/Fulfillment | Not implemented in current product schema |
| FR-007 | تفعيل/تعليق المتجر ومنع الطلبات من غير النشط | UC-02/06/08 | Stores/Orders | Active-store resolution exists; admin mutation absent |
| FR-008 | إنشاء المنتج وتعديله وأرشفته | UC-04 | Catalog | Entity exists; write APIs absent |
| FR-009 | إدارة تصنيفات المنتجات | UC-04 | Catalog | Entity exists; write APIs absent |
| FR-010 | الاسم والوصف والصور والسعر وSKU اختياري والحالة | UC-04/06 | Catalog | Model exists; write/media workflow absent |
| FR-011 | حالات Draft/Published/Archived | UC-04 | Catalog | Implemented in model |
| FR-012 | إخفاء المنتجات غير المنشورة عن الواجهة العامة | UC-06 | Catalog | Implemented in public query |
| FR-013 | إدخال OnHand ومتابعة Reserved/Available | UC-04 | Inventory | InventoryItem exists; adjustment APIs absent |
| FR-014 | منع حجز أكبر من المتاح عند التزامن | UC-04/08/10 | Inventory | Not implemented; RowVersion/model only |
| FR-015 | منتجات بسيطة دون Variants في MVP | UC-04/06 | Catalog | Scope constraint |
| FR-016 | تسجيل العميل ودخوله وخروجه داخل متجر | UC-05 | Identity | PoC only; not wired to Product Host |
| FR-017 | Customer account مستقل لكل متجر، ويسمح بتكرار البريد عبر متاجر مختلفة | UC-05 | Identity | PoC only |
| FR-018 | إدارة ملف العميل وعناوينه ضمن المتجر | UC-05 | Customers | Not implemented |
| FR-019 | عرض هوية المتجر ومنتجاته المنشورة | UC-06 | Storefront | Partial: public API only; no React UI/branding API |
| FR-020 | البحث والتصفية والترتيب والصفحات | UC-06 | Catalog | Implemented in public read API |
| FR-021 | تفاصيل المنتج والسعر والتوفر | UC-06 | Catalog/Inventory read | Implemented in public read API |
| FR-022 | سجل طلبات العميل ضمن المتجر | UC-05/09 | Customers/Orders | Not implemented |
| FR-023 | إضافة منتج للسلة وتعديل الكمية أو الحذف | UC-07 | Cart | Not implemented |
| FR-024 | السلة تحتوي منتجات متجر واحد | UC-07 | Cart | Not implemented |
| FR-025 | إعادة التحقق من المنتج والسعر والمخزون عند Checkout | UC-08 | Checkout | Not implemented |
| FR-026 | اختيار Delivery أو Pickup إذا كان متاحاً | UC-08 | Fulfillment | Settings exist; checkout absent |
| FR-027 | إدخال عنوان Delivery والتحقق من المنطقة | UC-08 | Fulfillment | Not implemented |
| FR-028 | الخادم يحسب الأسعار والرسوم والإجمالي | UC-08 | Pricing | Not implemented |
| FR-029 | إنشاء Order مع snapshots لاسم المنتج وسعره | UC-08/09 | Orders | Not implemented |
| FR-030 | حجز مخزون متسق ضمن معاملة | UC-08 | Inventory/Orders | Not implemented |
| FR-031 | Idempotent Checkout يمنع تكرار الطلب | UC-08 | Checkout | Not implemented |
| FR-032 | التاجر يعرض طلبات متجره ويبحث ويصفي | UC-09 | Merchant/Orders | Not implemented |
| FR-033 | تأكيد/رفض الطلب وفق قواعد المجال | UC-09 | Orders | Not implemented |
| FR-034 | تحديث التجهيز والتسليم عبر انتقالات مسموحة | UC-09 | Orders/Fulfillment | Not implemented |
| FR-035 | العميل يتابع حالة طلباته | UC-09 | Customers/Orders | Not implemented |
| FR-036 | سجل تاريخ الحالات والجهة التي غيرتها | UC-09/10 | Audit/Orders | Not implemented |
| FR-037 | إلغاء مسموح وتحرير Reservation مرة واحدة | UC-10 | Orders/Inventory | Not implemented |
| FR-038 | التاجر يعرف مناطق التوصيل ويفعلها/يعطلها | UC-03/09 | Fulfillment | Not implemented |
| FR-039 | رسم توصيل ثابت لكل منطقة في MVP | UC-08/09 | Fulfillment/Pricing | Not implemented |
| FR-040 | رفض التوصيل لمنطقة غير مدعومة | UC-08 | Fulfillment | Not implemented |
| FR-041 | Pickup فقط لمتجر Physical وخيار مفعّل | UC-03/08 | Stores/Fulfillment | Domain settings rule exists; checkout absent |
| FR-042 | الطلب يحفظ نوع التنفيذ وبيانات العنوان اللازمة وقت الطلب | UC-08/09 | Orders/Fulfillment | Not implemented |

التفصيل الكامل لكل معرف، وسائل التحقق، وأدلة مسارات المصدر موجود في `registers/requirements-traceability.csv`.

## 02.3 المتطلبات غير الوظيفية

| ID | المجال | مطلب قابل للتحقق | وسيلة التحقق |
|---|---|---|---|
| NFR-SEC-01 | Security | StoreId/AccountKind لا يثبتهما العميل دون تحقق خادمي | Negative API tests |
| NFR-SEC-02 | Security | لا تُسجل كلمات المرور أو Cookies أو Tickets أو connection strings | Log inspection + secret scan |
| NFR-TEN-01 | Isolation | لا قراءة/تعديل/حذف عبر حدود المتاجر | Cross-tenant SQL/API matrix |
| NFR-REL-01 | Reliability | Checkout/Cancel لا يتركان حالة Order/Inventory جزئية | Fault injection + transaction tests |
| NFR-CON-01 | Concurrency | لا تتجاوز الحجوزات الكمية المتاحة | SQL Server race tests |
| NFR-PERF-01 | Performance | Pagination وحدود حجم وفهارس مطابقة للاستعلامات | Query plans + load tests |
| NFR-MNT-01 | Maintainability | Domain مستقلة عن ASP.NET/EF/UI | Architecture tests + reference audit |
| NFR-UX-01 | Usability | واجهة متجاوبة وهوية مستقلة لكل متجر | Responsive E2E + usability review |
| NFR-AUD-01 | Auditability | تسجيل الموافقات والحالات والعمليات الحساسة | Audit tests |
| NFR-TST-01 | Testability | قواعد المجال والعزل والتزامن آلية | Test suite + CI evidence |
| NFR-OPS-01 | Operability | Logs/Metrics/Traces/Health/Runbooks | Staging verification |
| NFR-REC-01 | Recoverability | استعادة DB وData Protection keys ضمن RPO/RTO معتمدين | Restore drill |

P95، حمل الطلبات، عدد المتاجر، Availability، RPO/RTO، وميزانية التشغيل لم تُحسم بعد.

## 02.4 معايير قبول النسخة الأولى

| ID | معيار القبول | دليل الاعتماد |
|---|---|---|
| AC-01 | متجران تجريبيان برابط وهوية وإعدادات مختلفة | SQL + API + UI evidence |
| AC-02 | لا يمكن عبور المتجر بتغيير المعرف | Cross-tenant negative tests لكل مورد |
| AC-03 | العميل يتسوق في متجره فقط | Browser E2E عبر متجرين |
| AC-04 | Checkout COD بخوادم الأسعار والرسوم | Tampering + SQL transaction evidence |
| AC-05 | لا oversell لآخر قطعة عند طلبين متزامنين | SQL Server concurrency test |
| AC-06 | التاجر يعالج الطلب حتى التسليم أو الاستلام | Workflow E2E + history |
| AC-07 | تشغيل محلي ونشر تجريبي وAPI موثق واختبارات أساسية | Reproducible README + CI + Staging evidence |

## 02.5 متى يغلق المتطلب؟

يُغلق فقط عندما يكون واضحاً، مرتبطاً بسيناريو، له Acceptance Criteria، يغطيه اختبار إيجابي وسلبي، يوجد دليل تنفيذ في commit، ولا توجد فجوة أمن/عزل معروفة مرتبطة به. لا يغلق بناءً على الانطباع أو لقطة شاشة فقط.

## 02.6 Change Management

أي تغيير في FR/NFR/AC يحتاج Change Request يذكر السبب والقيمة والأثر على النطاق والبيانات وAPI/UI والأمن والاختبارات والترحيل والتراجع والوثائق. لا يُحذف مطلب بصمت.
