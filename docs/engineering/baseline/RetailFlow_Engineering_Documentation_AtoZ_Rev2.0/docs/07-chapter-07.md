# 07. الهوية والمصادقة والجلسات والأمن

## 07.1 فصل الحسابات

- `StoreCustomer`: حساب عميل مستقل داخل متجر محدد. يسمح من حيث النموذج باستخدام البريد نفسه في متجر آخر كحساب مختلف.
- `BackOffice`: هوية عامة لإدارة المنصة/التاجر، لكنها لا تمنح تلقائياً صلاحية على كل متجر؛ يجب وجود `StoreMembership` وفحص الدور/الصلاحية.
- `AccountKind` و`StoreId` لا يؤخذان من Body أو Header كمصدر موثوق. يحسم الخادم Scope من سياق الطلب والهوية المحققة.

## 07.2 حالة Identity وSession PoCs

في `experiments/IdentitySpike` يوجد Custom scoped UserStore وتجربة عزل `AccountKind` و`StoreId` ومخطط Identity تجريبي واختبارات SQL/Unit. وفي `experiments/SessionTransportSpike` توجد تجارب Cookie Authentication وStore scope وCSRF و`ITicketStore` وData Protection واختبارات Playwright. وثائق PoC تسجل نتائج تشغيل في سياقها مثل IdentitySpike `49/49`، SessionTransportSpike `90/90`، وPlaywright `4/4`.

هذه أدلة على PoCs، وليست تكاملاً في Product Host. لا يوجد اعتماد Product Host على مشاريع experiments في baseline، ولا يجوز أخذ PoC migrations باعتبارها مخطط إنتاجي دون مراجعة.

## 07.3 قرارات المصادقة والجلسات

- نموذج النشر Same-Origin للـ MVP: React وAPI على نفس scheme/host/port.
- Browser Cookie هي وسيلة النقل المفضلة في الدراسة، لكن `AUTH-ADR-01` بقي Proposed في baseline بسبب الحاجة إلى قياس الأداء وحسم سلوك تعدد المتاجر ومراجعة Production Readiness.
- لا تعتبر Cookie Path أو Cookie Name حداً أمنياً لعزل المتاجر.
- CSRF design: Custom Header `X-RetailFlow-Request: 1` + exact Origin أو Referer fallback + `Sec-Fetch-Site` عند وجوده، وSameSite دفاع إضافي.
- `ITicketStore` وPer-Session Revocation وData Protection shared key ring موثقة/مجرّبة في PoC، وليست مدمجة في Product Host.
- Idle 15 minutes وAbsolute 8 hours قيم PoC مسجلة؛ يلزم اعتمادها كسياسة منتج وأمن قبل الإنتاج.

## 07.4 HTTP behavior

- غير المصادق: 401 API response وليس Redirect إلى HTML login.
- المصادق دون صلاحية: 403.
- لا تعتمد على Accept Header وحده لضبط Cookie challenge behavior؛ افحص Endpoint metadata وTypedResults في .NET 10.
- Same-Origin لا يحتاج CORS بين React وAPI.
- كل Unsafe Method يخضع لسياسة CSRF بعد حسم route وOrigin pipeline.

## 07.5 Authorization contract

كل endpoint محمي يحدد:
1. AccountKind المطلوب.
2. Store scope المتوقع.
3. Membership/Role/Permission المطلوبة.
4. Resource ownership للمورد نفسه.
5. سلوك 401/403/404 ومنع تسريب وجود مورد في Tenant آخر.

Platform Support access يحتاج مساراً منفصلاً، least privilege، سبب دعم، صلاحية محدودة وتدقيق.

## 07.6 Threat Model

| التهديد | السيناريو | الضوابط |
|---|---|---|
| Cross-tenant IDOR | تغيير ProductId/OrderId | scoped queries + resource authorization + negative tests |
| Tenant context injection | إرسال StoreId مزيف | resolve slug على الخادم وتجاهل scope غير الموثوق |
| Login CSRF | إجبار متصفح على login بحساب غير صاحبه | Origin/Referer + Fetch Metadata + browser tests |
| CSRF | تغيير بيانات عبر Origin خارجي | Custom header + strict Origin + SameSite defense-in-depth |
| Session fixation/replay | إعادة Cookie قديمة بعد Logout | rotation/revocation + stale ticket tests |
| Brute force/enumeration | تخمين حسابات العملاء | rate limits + generic responses + lockout policy review |
| Secret leakage | تسجيل Cookie/Ticket/Connection String | structured log redaction + automated scan |
| Key loss | تعطل جميع الجلسات بعد restart | persistent protected shared key ring + backup/restore |
| Privilege escalation | Staff ينفذ Owner action | deny-by-default policy matrix + negative tests |
| XSS | تنفيذ script في storefront | output encoding + CSP + dependency and UI security review |

CSRF middleware لا يعالج XSS. يلزم CSP وinput/output protections.

## 07.7 Security Verification

استخدم OWASP ASVS 5.0.0 كأساس لمصفوفة تحقق. `registers/security-control-matrix.csv` هو سجل عمل أولي؛ يلزم لاحقاً ربط كل ضابط برقم ASVS exact، applicability، evidence، test result، owner وstatus. هذا الإصدار لا يدعي ASVS certification أو اختبار اختراق مستقل.
