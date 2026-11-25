# RetailFlow - Engineering Documentation A to Z (Rev 2.0)

## محتويات الحزمة
- `Master_Engineering_Handbook_AR.md`: الدليل الرئيسي من الحوكمة والرؤية والمتطلبات إلى التشغيل والصيانة.
- `Master_Engineering_Handbook_AR.docx` و`.pdf`: النسخ المنسقة للطباعة والتحرير.
- `docs/`: الفصول مفصولة بصيغة Markdown لتحديثها عبر Git.
- `registers/`: متطلبات، Acceptance Criteria، ADRs، قرارات مفتوحة، مخاطر، ضوابط أمنية، خارطة A-Z.
- `diagrams/`: C4-ish context/container/component وruntime/sequence/state/deployment/CI-CD بصيغة Mermaid.
- `api/openapi-current.yaml`: عقد Draft للمسارات الحالية الثلاثة فقط.
- `evidence/`: baseline implementation evidence وحدود المراجعة.
- `references/`: سجل المصادر الرسمية والملفات الأصلية.

## Baseline
Repository: `ayhm325/RetailFlow`  
Commit: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`  
Status: `Engineering Baseline Draft - Requires Owner Review`

## تنبيه أساسي
الوثائق تفرق بين Implemented وPoC وProposed وOpen Decision. لا تعتبر React أو Cart أو Checkout أو Orders أو Product Host Identity integration منفذة في baseline لمجرد أنها موثقة كهدف. راجع `evidence/baseline-evidence.md`.

## طريقة العمل الموصى بها
1. عدّل Markdown وregisters وdiagrams في branch.
2. راجع المعرفات وروابط التتبع.
3. حدّث commit baseline وأدلة الاختبار عند تغيير المصدر.
4. أنشئ PDF/Word من المصدر ضمن pipeline موثق.
5. راجع كل صفحة PDF قبل النشر.
