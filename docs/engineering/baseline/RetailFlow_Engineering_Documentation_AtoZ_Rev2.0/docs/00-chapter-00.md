# 00. حوكمة الوثائق وضبط الحالة

## 00.1 الغرض

هذه المجموعة هي المرجع الهندسي الموحد لـ RetailFlow، وتغطي دورة حياة المنتج من تعريف الرؤية والمتطلبات حتى المعمارية والتنفيذ والاختبار والنشر والتشغيل والصيانة. أُعدت اعتماداً على ملفات المشروع المرفقة، وفحص مصدر المستودع عند commit محدد، ومراجع هندسية رسمية ومهنية. وهي ليست شهادة ISO أو تقرير تدقيق خارجي.

## 00.2 Baseline الفعلي

- Repository: `ayhm325/RetailFlow`.
- Commit: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`، بتاريخ 25 نوفمبر 2026.
- SDK: `.NET SDK 10.0.404` في `global.json`.
- Product projects: `RetailFlow.Api`, `RetailFlow.Application`, `RetailFlow.Domain`, `RetailFlow.Infrastructure`.
- Test project الظاهر في الحل: `RetailFlow.Api.IntegrationTests`.
- مشاريع PoC مستقلة: `experiments/IdentitySpike` و`experiments/SessionTransportSpike`.
- الـ Product Host يحتوي Public Store Catalog Read API وSQL Server/EF Core catalog slice وStore resolution وhealth checks وProblemDetails.
- لا يظهر في هذا snapshot React `ClientApp`، ولا تكامل Identity/Session داخل Product Host، ولا Merchant CRUD، ولا Customer profile، ولا Cart/Checkout/Orders، ولا Reservation workflow، ولا Deployment workflow للإنتاج.

**حدود الدليل:** فُحصت شجرة المصدر وملفات مختارة عبر GitHub. لم يتم تشغيل build أو tests من جديد خلال إعداد هذا الدليل. حالة GitHub commit status التي ظهرت عند المعاينة كانت `pending` دون check entries؛ لذلك لا يدعي الدليل أن هذا commit اجتاز CI.

## 00.3 وسوم الحالة

| الوسم | التعريف | قاعدة الادعاء |
|---|---|---|
| Implemented | كود منتج موجود في Product Host عند commit المرجعي | اذكر مسار الملف والاختبار عند توفره |
| PoC | موجود داخل تجربة مستقلة | لا يقدم على أنه ميزة في Product Host |
| Proposed | تصميم أو توصية لم تدمج أو لم تعتمد نهائياً | لا يُقال إنها تعمل |
| Open Decision | تحتاج قراراً من Product/Architecture/Security/Ops Owner | تبقى مانعاً للقرارات التابعة |
| Verified | يوجد دليل تشغيل/اختبار على commit وبيئة محددين | اذكر commit/run/environment لا وصفاً عاماً |

## 00.4 ترتيب حجية المصادر

1. كود Product Host والاختبارات عند commit محدد لإثبات التنفيذ.
2. ADR معتمد وحديث لإثبات قرار التصميم.
3. وثائق مراحل RetailFlow الأصلية لتحديد الرؤية والمتطلبات والنطاق والقرارات التاريخية.
4. هذا الدليل لإعادة تنظيم المصادر وإضافة المقترحات والفجوات بوسوم واضحة.
5. المراجع الخارجية للاستدلال والتحقق، لا لاستبدال قرار صاحب المنتج.

إذا تعارضت المصادر، لا يُصحح التعارض بصمت. يُفتح Change Request، ويُحدّث ADR، وتُحدّث المعرفات والاختبارات والوثائق المرتبطة.

## 00.5 ضبط الإصدارات والاعتماد

- Document set: RetailFlow Engineering Documentation A to Z.
- Version 2.0.0.
- Status: Engineering Baseline Draft - Requires Owner Review.
- اللغة: العربية مع الاحتفاظ بالمصطلحات التقنية الإنجليزية.
- المرجع البرمجي ثابت في هذا الإصدار، ويجب تحديثه بعد كل release.
- كل وثيقة مستقبلية تتضمن Owner وApprover وStatus وVersion وLast Reviewed وRelated Requirements/ADRs.

## 00.6 الأدوار

| الدور | المسؤولية |
|---|---|
| Product Owner | الرؤية، نطاق MVP، الأولويات، السياسات التجارية والقرارات المفتوحة |
| Architecture Owner | حدود النظام، ADRs، الاتساق المعماري، مراجعة التبعيات |
| Backend Owner | Domain/Application/API/Persistence والعقود والبيانات |
| Frontend Owner | تجربة React، إدارة الحالة، التكامل والـ accessibility |
| QA Owner | استراتيجية الاختبار وأدلة القبول وإدارة العيوب |
| Security Reviewer | Threat Model، التحقق الأمني، ASVS mapping |
| Operations Owner | البنية، النشر، المراقبة، الاستعادة والاستجابة للحوادث |

يمكن لشخص واحد أن يؤدي هذه الأدوار في مشروع فردي؛ لكن ينبغي أن تبقى قراراتها ومسؤوليات اعتمادها واضحة.

## 00.7 قواعد عدم الادعاء

- نجاح PoC لا يعني اكتمال المنتج.
- وجود Entity/DbSet لا يعني أن Use Case قابلة للاستخدام.
- وجود Workflow في GitHub لا يعني أن commit نجح في CI.
- Staging لا تعني Production Ready.
- استخدام معيار لا يعني شهادة مطابقة له.
- لا تضع أرقام SLO/RPO/RTO/Performance قبل قياس واعتماد.
