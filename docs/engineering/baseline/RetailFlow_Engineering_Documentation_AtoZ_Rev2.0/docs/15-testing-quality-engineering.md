# 15. الاختبار وضمان الجودة (Testing & Quality Engineering)

## 15.1 فلسفة الاختبار

لا يُعد نجاح Build أو عدد اختبارات مرتفع دليلاً كافياً على صحة المنتج. يجب أن تثبت الاختبارات السلوك التجاري، وحدود الثقة، وقيود البيانات، وسلامة العمليات المتزامنة. تُربط كل نتيجة ببيئة التنفيذ ونسخة الكود وإعدادات قاعدة البيانات ووقت التشغيل.

## 15.2 مستويات الاختبار

| المستوى | الهدف | أمثلة RetailFlow | بوابة القبول |
|---|---|---|---|
| Unit | قواعد المجال والمنطق المعزول | MoneyPrecision، StoreSettings، transitions، reservation invariants | نتائج حتمية بلا SQL/شبكة |
| Application | تنسيق Use Cases | Checkout orchestration، authorization decisions، cancellation | mocks/fakes للحدود الخارجية عند الحاجة فقط |
| Infrastructure Integration | السلوك الفعلي مع SQL Server | FK، CHECK، indexes، transactions، rowversion، migrations | SQL Server حقيقي أو container مماثل للإنتاج |
| API Integration | HTTP contract وpipeline | route resolution، ProblemDetails، auth، validation | WebApplicationFactory + قاعدة اختبار معزولة |
| Browser E2E | رحلة المستخدم | multi-store tabs، cart، checkout، merchant order | Chromium/Playwright مع API فعلي وHTTPS |
| Concurrency | سباقات الطلبات | آخر وحدة مخزون، حجز/إلغاء/دفع متكرر | assertions على النتيجة النهائية في SQL |
| Performance | قدرة واستقرار | catalog paging/search، checkout contention | workload وbaseline معلنان |
| Security | إساءة الاستخدام والحدود | IDOR، tenant escape، CSRF، brute force | اختبارات إيجابية وسلبية مع Evidence |
| Resilience/Recovery | فشل واعادة تشغيل | SQL unavailable، interrupted checkout، restore | إجراءات قابلة لإعادة التنفيذ وتوثيق النتائج |

## 15.3 مصفوفة السيناريوهات الحرجة

| ID | السيناريو | النتيجة المتوقعة | الدليل |
|---|---|---|---|
| TST-TEN-001 | تغيير StoreId في body أو query | لا يغيّر scope المحسوم من slug/الهوية؛ يُرفض أو يُتجاهل وفق العقد | API integration |
| TST-TEN-002 | استخدام ProductId من متجر آخر | لا يعاد سجل المتجر الآخر | SQL/API integration |
| TST-INV-001 | طلبان متزامنان لآخر كمية | حجز واحد فقط ينجح ولا تصبح Available سالبة | SQL concurrency test |
| TST-INV-002 | تكرار Release لنفس reservation | لا يحدث تحرير مزدوج | domain + SQL integration |
| TST-ORD-001 | Checkout مع تغيير السعر بعد عرض السلة | الخادم يعيد الحساب ويثبت snapshot الصحيح | application/API integration |
| TST-ORD-002 | تكرار POST Checkout بنفس Idempotency-Key | نفس النتيجة/Order دون طلب مكرر | API integration |
| TST-ORD-003 | فشل حفظ Order بعد الحجز | Rollback أو تعويض موثق؛ لا يتبقى حجز يتيم | SQL integration |
| TST-AUTH-001 | Cookie متجر A على مسار متجر B | رفض دون تسريب | browser E2E |
| TST-CSRF-001 | POST من Origin غير موثوق | رفض قبل تنفيذ تغيير الحالة | browser E2E |
| TST-SES-001 | Logout أثناء renewal | لا تعود جلسة ملغاة للحياة | concurrency + browser |
| TST-REL-001 | Restore إلى بيئة معزولة | البيانات والمفاتيح قابلة للاستعادة والتحقق | recovery drill |
| TST-API-001 | API unknown path | 404/ProblemDetails، وليس SPA index | API integration |

## 15.4 قواعد جودة الاختبارات

- الاختبار يجب أن يفشل عند كسر المتطلب، لا أن يكرر implementation detail فقط.
- تجنب مشاركة بيانات اختبار mutable بين الاختبارات المتوازية.
- استخدم SQL Server لأن SQLite/InMemory لا يثبتان سلوك SQL Server constraints أو rowversion أو query translation.
- اجعل كل اختبار concurrency يقرأ الحالة النهائية من قاعدة البيانات، لا يكتفي بعدد HTTP responses.
- اختبر upgrade من نسخة سابقة وrollback حيث تدعمه خطة النشر؛ لا تُجرِ rollback تلقائياً إذا كانت migration غير عكسية أو فقد البيانات محتمل.
- خزّن TRX، Playwright traces/screenshots عند الفشل، logs منقحة، commit SHA، ونسخة SDK/DB في CI artifacts.
- افصل flaky tests عن الاختبارات المستقرة، ولا تستخدم retry لإخفاء سباق حقيقي.

## 15.5 Quality Gates

| البوابة | الشرط |
|---|---|
| G-QA-01 | Build Release ناجح لكل الحلول المستهدفة |
| G-QA-02 | Unit + Integration + E2E المطلوبة للـ slice ناجحة |
| G-QA-03 | لا توجد ثغرات Critical/High غير مقبولة في نطاق الإصدار |
| G-QA-04 | tenant isolation وinventory invariants مثبتة باختبارات سلبية ومتزامنة |
| G-QA-05 | Migration upgrade على نسخة بيانات ممثلة ومراجعتها |
| G-QA-06 | جميع المتطلبات ضمن release scope لها test evidence أو deviation معتمد |
| G-QA-07 | تشغيل smoke + rollback/restore plan مثبت في Staging |
