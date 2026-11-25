# 19. الإصدارات والدعم والحوكمة والامتثال

## 19.1 Versioning

- الوثائق لها Document ID، Version، Status، Owner، Effective Date، Source Commit، Reviewers.
- قرارات الهندسة ADR مرقمة ولا تُمحى عند التغيير؛ يصدر ADR جديد superseding القرار السابق.
- API contract يتغير وفق compatibility policy. لا تكسر العملاء بصمت؛ وثّق deprecation window.
- استخدم Semantic Versioning للمكتبات/العقود حيث يلائم، وحدد Release version للمنتج.

## 19.2 Release Checklist

- نطاق الإصدار ومتطلباته معلوم.
- traceability مكتملة للمتطلبات داخل الإصدار.
- migration review + backup check.
- security review للميزات ذات المخاطر.
- tests وperformance baseline مناسبة.
- secrets/config/health endpoints جاهزة.
- rollout/rollback أو forward-fix plan واضح.
- runbooks/support جاهزة.
- موافقة Product/Engineering حسب حجم المخاطرة.

## 19.3 Support & Customer Operations

قبل إطلاق متاجر حقيقية، حدد:
- قنوات دعم التاجر وأوقات الخدمة.
- من يستطيع دعم متجر معين وكيف يتم التحقق من هوية طالب الدعم.
- سياسة Platform Admin access إلى بيانات المبيعات، break-glass access وتسجيل السبب والمدة.
- مسار تعطيل متجر/حساب مشتبه به دون حذف سجل الطلبات.
- آلية شكاوى الطلبات والاسترجاع والإلغاء والنزاع النقدي COD.
- عمليات استعادة الحساب، تصعيد الحوادث، إخطار التجار عند الانقطاع.

## 19.4 Legal/Commercial Readiness

المستندات التقنية لا تغني عن مراجعة قانونية/تجارية. قبل Production في السوق المستهدف راجع Terms of Service، Privacy Notice، Merchant Agreement، customer terms، refund/cancellation policy، cookie notice إذا انطبق، متطلبات الفوترة والضرائب، حماية المستهلك، وحماية البيانات. لا تقرر متطلبات تشريعية دون مستشار محلي مطلع على نشاط المنتج ومكان تشغيله.
