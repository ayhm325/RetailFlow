# 16. نموذج التهديد والأمن والخصوصية (Security, Privacy & Threat Model)

## 16.1 حدود الثقة

المتصفح ومدخلات المستخدم ومحتوى الصور وعناوين التسليم وslug وQuery Parameters مصادر غير موثوقة. لا تصبح قيمة موثوقة لأنها جاءت من React أو لأن المسار يحتوي slug صحيحاً. مصدر الحقيقة للـ Store Context هو resolver خادمي، ومصدر الحقيقة للملكية والصلاحية هو التحقق الخادمي من العلاقة والـ policy.

## 16.2 أصول يجب حمايتها

| الأصل | الخطر الرئيسي | الضوابط الأساسية |
|---|---|---|
| بيانات متجر ومنتجاته وطلباته | Cross-tenant disclosure/modification | StoreId predicates + composite FKs + authorization + tests |
| بيانات العملاء وعناوينهم | PII exposure/IDOR | resource authorization + least privilege + retention |
| كلمات المرور وبيانات Identity | credential theft | ASP.NET Core Identity settings + rate limiting + secure recovery |
| Session tickets / Data Protection keys | session replay/forgery | HttpOnly/Secure cookies، server-side revocation حيث يعتمد، key protection/rotation |
| المخزون والحجوزات | oversell/double release | atomic DB operation، rowversion/locking، constraints، idempotency |
| أسعار وOrder snapshots | price tampering | server-side pricing، immutable snapshots، audit trail |
| Connection strings/secrets | account takeover/data breach | managed secret store، no secrets in repo/logs |
| CI/CD identity | supply-chain/deploy compromise | OIDC، least privilege، protected environments |

## 16.3 STRIDE-style threat register

| Threat ID | التهديد | السيناريو | الضوابط/الاختبار |
|---|---|---|---|
| THR-001 | Spoofing | استخدام Customer session في متجر آخر | bind AccountKind/StoreId مع canonical store scope، browser tests |
| THR-002 | Tampering | تعديل price/fee/StoreId في الطلب | إعادة الحساب خادمياً، ignore client totals، integration tests |
| THR-003 | Repudiation | إنكار تغيير حالة طلب | actor + timestamp + reason + status history، audit integrity |
| THR-004 | Information Disclosure | IDOR على Product/Order/Address | tenant-scoped lookup/resource auth، negative tests |
| THR-005 | Denial of Service | catalog search أو login flood | pagination caps، rate limits، timeouts، query budgets |
| THR-006 | Elevation of Privilege | Store Staff ينفذ Owner-only action | deny-by-default policies + membership status/role checks |
| THR-007 | CSRF | Cookie-authenticated cross-site POST | custom header + strict Origin/Referer + Fetch Metadata وفق ADR المعتمد |
| THR-008 | Session replay | تذكرة قديمة بعد Logout/password change | server-side revocation، concurrency tests |
| THR-009 | Secret leakage | CI logs أو appsettings تسرب credential | secret scanning، masked logs، managed secrets |
| THR-010 | Supply-chain compromise | dependency/action متغير أو غير موثوق | pin versions/SHA، dependency review، artifact provenance |

## 16.4 ضوابط أمنية مطلوبة قبل Production

- ASP.NET Core Identity: سياسة كلمة المرور وLockout وEmail confirmation/Reset وAccount enumeration حسب قرار المنتج ومزود البريد.
- Rate limiting على Login/Register/Password change والعمليات المكلفة، مع مفاتيح partition آمنة ومراقبة false positives.
- Authorization: لا تعتمد على إخفاء زر في React؛ كل endpoint خاص يفرض policy/resource check في الخادم.
- CSRF: لا تفعّل cookie authentication في Product Host قبل اعتماد ADR نهائي وإغلاق اختبارات المتصفح. CSRF الحالي في PoC لا يعني تكامله في Product Host.
- XSS: encoding افتراضي، sanitization للمحتوى الغني إن أضيف، CSP تدريجية، منع HTML غير موثوق، وعدم تخزين token في Web Storage.
- CORS: Same-Origin هو اتجاه MVP في ADR؛ لا تفتح origins عامة مع credentials. أي تغيير يحتاج ADR جديد واختبارات.
- SSRF: لا تسمح بجلب صورة من URL عشوائي على الخادم دون allowlist/egress controls؛ يفضّل upload مباشر إلى storage بعد تحقق المحتوى.
- Uploads: content-type sniffing، الحجم والأبعاد، أسماء عشوائية، تخزين خارج webroot، malware scanning عند ملاءمته، وعدم الوثوق بالامتداد.
- Logging: لا تسجل passwords أو cookie values أو tickets أو connection strings أو بيانات الدفع الحساسة. استخدم correlation IDs دون PII غير لازمة.
- Data Protection: مفاتيح دائمة ومشتركة حسب topology، encrypted at rest، rotation، backup/restore، وفصل dev/staging/prod.
- Database: least-privilege service account، TLS، encrypted backups، patching، no production SA account.

## 16.5 الخصوصية وإدارة البيانات

أنشئ Data Inventory يحدد لكل حقل: الغرض، التصنيف، المالك، مكان التخزين، جهات الوصول، مدة الاحتفاظ، وسلوك الحذف. صنّف بيانات العنوان والهاتف وسجل الطلبات كبيانات شخصية أو حساسة تشغيلياً بحسب القانون والعقد المطبق.

قرارات يجب اعتمادها قبل جمع بيانات حقيقية:
- سياسة الخصوصية والموافقة/الأساس القانوني المناسب للسوق المستهدف.
- مدة الاحتفاظ بالحساب والعناوين والطلبات والسجلات.
- طلبات الوصول والتصحيح والحذف مع مراعاة الالتزامات المحاسبية/التجارية.
- نقل البيانات خارج الأردن أو عبر مزودي Cloud/Email/SMS.
- بيانات الأطفال أو المنتجات المنظمة إن دخلت نطاق المنتج.
- Incident notification وData breach handling وفق القانون المنطبق بعد مراجعة قانونية محلية.

لا تدّعِ الامتثال لقانون حماية البيانات الأردني أو GDPR قبل مراجعة قانونية موثقة لنطاق التشغيل الفعلي ومزودي الخدمة والبيانات.

## 16.6 Security Acceptance

يرتبط Security Acceptance بـ OWASP ASVS 5.0.0 كمرجع تحقق، مع اختيار مستوى ومتطلبات مناسبة بناء على threat model. لا تعني الإشارة إلى ASVS أن جميع متطلباته مطبقة. أنشئ mapping لكل control إلى implementation، test، evidence، status، exception owner.
