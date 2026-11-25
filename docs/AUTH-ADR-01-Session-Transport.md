# AUTH-ADR-01 — استراتيجية نقل جلسات المصادقة في RetailFlow

| الحقل | القيمة |
|---|---|
| **الحالة** | **Proposed — Session Transport Spike قيد التنفيذ (CSRF + Per-Session `ITicketStore` + `TimeProvider` + `ExpiredSessionTicketCleanupService` + `Shared Data Protection Key Ring` منفذة ومختبرة؛ متبقي `Explicit Session Expiration` و`403 Authorization Policy` وPlaywright وقياس الأداء)** |
| **الإصدار** | 0.8 |
| **التاريخ** | 25 نوفمبر 2026 |
| **النطاق** | Browser Authentication · StoreCustomer · BackOffice boundary |
| **المنصة** | .NET 10 · ASP.NET Core Identity · React + TypeScript · SQL Server |
| **القرار المرتبط** | Phase 06 — Identity PoC |
| **القرار السابق** | لا يُفعّل Authentication في التطبيق الفعلي قبل إغلاق هذه الوثيقة وبوابة الاختبار |

> **ملخص القرار:** يعتمد RetailFlow توجيه المتاجر عبر `Path-Based URLs` في MVP وفق وثيقة المنتج. وتُفضّل `HttpOnly Cookie Authentication` مبدئياً لتطبيق React Browser SPA، لكن لا تُعتمد آلية الجلسات نهائياً قبل إثبات استقلال جلسات المتاجر في المتصفح نفسه، ونجاح إصدار الكوكي وتجديدها وحذفها، وفحص CSRF وCORS وطوبولوجيا نشر الـ API. لا يُسمح باعتبار Cookie `Path` حدّاً أمنياً لعزل المستأجرين.

---

## 1. المشكلة والقرار المطلوب

يحتاج RetailFlow إلى وسيلة موثوقة لحمل جلسة المصادقة بين React SPA وASP.NET Core Web API، مع الحفاظ على فصل حسابات العملاء بين المتاجر.

المطلوب ليس اختيار آلية تسجيل دخول فقط؛ بل تحديد كيفية إنشاء الجلسة، وإرسالها، والتحقق منها، وتجديدها، وإنهائها، وإبطالها، وكيف تتفاعل مع `IdentityLookupScope` و`StoreRequestContext`.

يجب أن يسمح التصميم بما يلي:

1. استخدام البريد الإلكتروني نفسه لإنشاء حساب عميل مستقل في متجرين مختلفين، مع بيانات اعتماد وحالة أمنية مستقلتين.
2. منع استخدام جلسة أو هوية متجر A في مسارات متجر B.
3. عدم تحويل حساب `BackOffice` العالمي إلى حساب عميل مرتبط بمتجر.
4. دعم React Browser SPA دون وضع أسرار المصادقة في `localStorage` أو `sessionStorage`.
5. توفير سلوك واضح للتجديد وLogout وتغيير كلمة المرور وتعطيل الحساب.
6. الحفاظ على أداء قابل للقياس، وتجنب خدمة Token أو تخزين جلسات موزع ما لم تبرره متطلبات مثبتة.

## 2. سياق RetailFlow المثبت في الوثائق

### 2.1 نموذج المنتج

- RetailFlow تطبيق مشترك متعدد المستأجرين، وليس مشروعاً منفصلاً لكل متجر.
- يستخدم العملاء واجهة متجر مستقلة برابط خاص، مع مشاركة React Components وقواعد الأعمال.
- قاعدة الـ MVP تعرض رابطاً من نمط `retailflow.app/{store-slug}`، بينما تؤجل Custom Domains إلى مرحلة لاحقة.
- الـ Backend والبيانات مشتركة تشغيلياً، ويُفرض عزل المتاجر داخل الخادم.
- العميل يملك حساب `StoreCustomer` مستقلاً لكل متجر؛ وحساب BackOffice عالمي، وتدار صلاحياته عبر `StoreMembership`.

### 2.2 واجهات API الحالية في التصميم

```text
POST /api/stores/{slug}/customers/register
POST /api/stores/{slug}/customers/login
POST /api/stores/{slug}/customers/logout
GET  /api/stores/{slug}/customers/me
```

يُحل `slug` على الخادم إلى متجر نشط و`StoreId` موثوق. ولا يقبل الخادم `StoreId` القادم من Body أو Header باعتباره دليلاً على صلاحية الوصول.

### 2.3 الحالة الحالية للتنفيذ

- `IdentityScopeResolver` و`IdentityScopeMiddleware` و`ScopedUserStore` منفذة ومختبرة ضمن Identity Spike (مع إغلاق فجوات خصائص المستخدم في `ScopedUserStore` ونجاح Regression **`49 / 49`**).
- Route Group الخاص بعملاء المتاجر وRoute Group الخاص بـ BackOffice مربوطان في `Program.cs`.
- أُنشئت تجربة [`SessionTransportSpike`](../experiments/SessionTransportSpike/RetailFlow.SessionTransportSpike.slnx) المعزولة لإثبات نقل الجلسات بالـ Cookies لكل متجر وحماية CSRF، ونجحت اختبارات **`69 / 69`** (`57` Unit + `12` Integration على SQL Server و`WebApplicationFactory`).
- `AddAuthentication` و`UseAuthentication` و`UseAuthorization` وHTTP Authentication endpoints لم تُعتمد بعد في التطبيق الفعلي لـ `IdentitySpike` بانتظار استكمال بقية بوابات القبول في هذا ADR.
- هذا القرار لا يغيّر Identity Schema أو UserStore أو Scope model.

### 2.4 تسوية صياغة Technology Stack بين الوثائق

يذكر دليل المشروع التأسيسي في جدول Backend عبارة `ASP.NET Core Identity + JWT`. أما دراسة Phase 06 الأكثر تخصصاً في Identity فتؤجل `AUTH-ADR-01` صراحةً وتقارن `HttpOnly Cookies` و`Standard JWT` و`Identity Bearer Tokens` قبل التنفيذ.

لذلك تُعامل عبارة JWT في الجدول التأسيسي باعتبارها **توجهاً تقنياً أولياً غير كافٍ وحده لإغلاق قرار Session Transport**. هذه الوثيقة هي سجل القرار المتخصص الذي يجب أن يحسم الوسيلة بعد الـ Spike. بعد قبول القرار النهائي، يجب تحديث جدول Technology Stack أو إضافة إحالة صريحة إلى ADR حتى لا تبقى الوثيقتان قابلتين لتفسيرين مختلفين.

## 3. المصطلحات وحدود المسؤولية

| المصطلح | المسؤولية |
|---|---|
| `IdentityLookupScope` | يحدد حسابات Identity التي يجوز البحث عنها في الطلب الحالي. |
| `StoreRequestContext` | يحدد المتجر التجاري الذي يستهدفه الطلب. |
| `Authentication` | يتحقق من وسيلة إثبات الجلسة ويبني `ClaimsPrincipal`. |
| `Authorization` | يقرر هل يملك المستخدم المصادق عليه صلاحية العملية المطلوبة. |
| `Session Transport` | يحدد كيف تنتقل بيانات الجلسة بين المتصفح والـ API. |

لا يجوز دمج هذه المسؤوليات. وجود Cookie أو JWT صالح لا يثبت أن صاحبه مخوّل للوصول إلى متجر الطلب الحالي.

## 4. المتطلبات والقيود

### 4.1 متطلبات وظيفية

- `StoreCustomer` مرتبط بمتجر واحد فقط.
- `BackOffice` حساب عالمي، والوصول إلى متجر من خلاله يحتاج فحص `StoreMembership` وصلاحياته.
- يجب اختبار استخدام البريد نفسه في متجرين بكلمتي مرور مختلفتين.
- يجب تحديد ما إذا كان المطلوب هو **تبديل الحساب بين المتاجر** أم **تشغيل جلستين مستقلتين بالتزامن في Tabs ضمن المتصفح نفسه**. لا تعتبر الوثيقة هذا التفصيل محسومًا قبل اختبار القبول.
- Logout من متجر لا يجوز أن ينهي جلسة متجر آخر إذا كان دعم الجلسات المتزامنة ضمن نطاق المنتج.

### 4.2 متطلبات أمنية

- HTTPS إلزامي لكل جلسة؛ وCookie المصادقة يجب أن تحمل `Secure` و`HttpOnly`.
- لا تحفظ Access Tokens أو Refresh Tokens أو Session IDs في `localStorage` أو `sessionStorage`.
- حماية CSRF مطلوبة لأي آلية تعتمد على اعتماد يرسله المتصفح تلقائياً.
- يجب التحقق من تطابق `AccountKind` و`StoreId` في هوية المستخدم مع Scope الذي حُسم من مسار الطلب.
- لا تُعامل `StoreSlug` أو Cookie Path أو CORS كبديل عن Authorization وفحوص Tenant Isolation داخل الخادم.
- لا تسجل كلمات المرور أو Tokens أو Cookies أو محتوى الجلسة في Logs.

### 4.3 متطلبات الأداء والتشغيل

- لا تُضاف مكالمة شبكة أو خدمة Token خارجية لكل طلب API.
- لا يُضاف استعلام SQL لكل طلب لمجرد تحديد وسيلة المصادقة، إلا إذا قررت متطلبات الإبطال الفوري ذلك صراحة.
- يجب قياس P50/P95 وThroughput وAllocated Bytes وSQL command count لكل بديل.
- يجب أن يعمل التحقق من الجلسة على أكثر من API Instance بعد ضبط Data Protection أو مفاتيح التوقيع حسب البديل.
- يجب فصل تكاليف Authentication عن استعلامات المجال التجاري عند القياس.

## 5. الطوبولوجيا المعتمدة مبدئياً

### 5.1 MVP: Path-Based

```text
https://retailflow.app/store-a
https://retailflow.app/store-b

https://retailflow.app/api/stores/store-a/customers/...
https://retailflow.app/api/stores/store-b/customers/...
```

**حالة القرار:** توجه موثق في Product Blueprint للـ MVP.

### 5.2 قرار نشر ما زال يحتاج تثبيتاً

وثيقة المنتج تثبت شكل رابط المتجر، لكنها لا تثبت بصورة قاطعة كيف سيُستضاف React وAPI بالنسبة إلى Origin واحد.

**التوصية التشغيلية للـ MVP:** نشر React والـ API تحت Origin واحد، وتوجيه `/api/*` إلى ASP.NET Core عبر Reverse Proxy أو استضافة الـ SPA والـ API خلف نفس Host. في التطوير المحلي يستخدم Vite Proxy لمسار `/api`.

أسباب التوصية:

- تجنب CORS في مسار المتصفح الأساسي.
- تجنب اعتماد مصادقة المتصفح على Cross-Origin Credentials.
- تبسيط Cookie وAntiforgery وعمليات Logout.
- تقليل عدد نقاط الإعداد والتشغيل التي يمكن أن تتسبب في فشل الجلسة.

هذه **توصية معمارية** وليست حقيقة مثبتة من ملفات المشروع؛ ويجب مطابقتها مع قرار الاستضافة الفعلي قبل قبول ADR.

إذا فُصل Frontend عن API على Origins مختلفة، يجب إعادة تقييم Cookie `SameSite` و`Secure` وCORS و`credentials` وقيود المتصفحات على Third-Party Cookies. يمنع استخدام `AllowAnyOrigin` مع `AllowCredentials`.

## 6. البدائل المدروسة

### البديل A — ASP.NET Core Cookie Authentication

يصدر الخادم Authentication Ticket محمياً بـ Data Protection داخل Cookie. يرسل المتصفح Cookie تلقائياً للطلبات التي تطابق Domain وPath وSameSite وسياسات المتصفح.

**المزايا**

- توصي Microsoft باستخدام Cookies في تطبيقات Browser SPA؛ المتصفح يديرها تلقائياً، و`HttpOnly` يمنع JavaScript من قراءة قيمة Cookie مباشرة.
- يتكامل طبيعياً مع ASP.NET Core Authentication وIdentity و`SignInManager`.
- لا يحتاج إلى بناء JWT Issuer أو Refresh Token Service لمجرد مصادقة Browser SPA.
- يمكن حماية Cookie Ticket باستخدام Data Protection، مع Key Ring دائم ومشترك عند تعدد النسخ.

**التكاليف والمخاطر**

- CSRF؛ لأن المتصفح يرفق Cookie تلقائياً.
- CORS وCross-Origin Credentials إذا كان React وAPI على Origins مختلفة.
- Cookie عامة واحدة لا تدعم حسابين مستقلين على نفس Host في الوقت نفسه؛ تسجيل الدخول الثاني قد يستبدل الجلسة الأولى.
- استخدام Cookie واحدة بالاسم نفسه مع `Path` مختلف لكل متجر يزيد تعقيد إدارة الكوكيز، ولا يمثل حدّاً أمنياً لعزل المستأجرين.
- يجب اختبار مسارات الإصدار والتجديد والحذف، لا Login وحده.

### البديل B — Standard JWT Bearer

يصدر النظام Access Token بصيغة JWT قياسية وموقعة، ويرسلها React في `Authorization: Bearer`.

**المزايا**

- كل متجر يمكن أن يحصل على Access Token مستقل في ذاكرة التطبيق.
- لا يرفق المتصفح Bearer Token تلقائياً؛ يلزم وضعه صراحة في Authorization Header.
- يناسب حالات وجود Mobile Apps أو API Consumers آخرين أو أكثر من Resource Server.
- التحقق من التوقيع والـ Issuer والـ Audience يمكن أن يكون محلياً بعد تحميل مفاتيح التحقق، دون استعلام DB لكل طلب.

**التكاليف والمخاطر**

- JWT ليس نظام جلسات متكاملاً؛ يلزم تصميم Access Token Lifecycle وRefresh Token Rotation وRevocation وSigning Key Rotation وIssuer/Audience.
- تخزين Tokens في Web Storage غير مقبول في هذا القرار؛ ووجودها في ذاكرة React يقلل الاستمرارية بعد Reload ويظل معرضاً لقدرة XSS على تنفيذ عمليات باسم المستخدم.
- Refresh Token آمن للـ Browser يعيد إدخال تحديات Cookie وCSRF، أو يتطلب BFF/Authorization Server مناسباً.
- الإبطال الفوري لـ Access Token يحتاج حالة خادمية أو مدة صلاحية قصيرة جداً مع تبعات UX، ولا يتحقق تلقائياً من JWT.

### البديل C — ASP.NET Core Identity Bearer Tokens

توفر Identity API المدمجة خيار Tokens مملوكة لـ ASP.NET Core Identity، لكنها **ليست Standard JWT** وليست Full-featured Token Server. لا تُختار إذا كان مطلب المنتج أو أي عميل خارجي يشترط JWT قياسياً أو OAuth/OIDC.

كما أن `MapIdentityApi` لا يُستخدم كما هو في تدفقات RetailFlow دون التخصيص والاختبار المحددين في دراسة Phase 06، بسبب اختلاف مسارات البحث والتسجيل عن سياسة Email + Scoped UserStore.

### البديل D — BFF / Server-Side Multi-Store Session

يحتفظ Backend بجلسة Browser محمية، ويدير على الخادم سجلات جلسات مستقلة لكل `(BrowserSession, StoreId)`.

**المزايا:** يمكن أن يدعم أكثر من حساب StoreCustomer في المتصفح نفسه دون وضع JWTs في Web Storage.

**التكاليف:** يتطلب Session Store وربطاً مخصصاً بين Browser Session وStore Identity، وتحديد عمر كل جلسة وإبطالها، واختبار التزامن والاتساق. وقد يضيف Cache/Store lookup إلى مسار الطلب. لا يُعتمد لمجرد الرغبة في نمط معماري متقدم؛ لا يستخدم إلا إذا كان تعدد الجلسات المتزامنة شرطاً فعلياً ولم ينجح بديل أبسط.

### البديل E — Subdomain Per Store

```text
https://store-a.retailflow.app
https://store-b.retailflow.app
```

يمكن استضافة نفس تطبيق React على كل Host وتوجيه API تحت نفس Host، مع Host-only Cookie لكل متجر دون `Domain` مشترك.

هذا يمنح فصلاً أوضح على مستوى Origin/Host للمتاجر، ويسهّل Cookie Isolation مقارنةً بمحاولة استخدام Cookie اسمها واحد مع Paths متعددة. لكنه يغير قرار URL الخاص بالـ MVP ويتطلب مراجعة DNS وTLS وRouting وCORS عند فصل API أو دعم Custom Domains.

**الحالة:** بديل معماري احتياطي، وليس تغييراً تلقائياً لقرار Product Blueprint.

## 7. ملاحظة تنفيذية حرجة من مصدر ASP.NET Core 10

تمت مراجعة مصدر `CookieAuthenticationHandler` في `release/10.0`، وليس الاعتماد على وصف عام للـ Cookie فقط:

1. `HandleSignInAsync` يبني Cookie Options، ثم يستدعي `Events.SigningIn`، وبعدها يرسل Cookie باستخدام `signInContext.CookieOptions`.
2. `HandleSignOutAsync` يستدعي `Events.SigningOut`، ثم يحذف Cookie باستخدام `context.CookieOptions`.
3. مسار Sliding Renewal داخل `FinishResponseAsync` يعيد بناء Cookie Options عبر `BuildCookieOptions()` ثم يرسل Cookie مباشرة؛ لا يمر بمسار `SigningIn` نفسه.

**النتيجة:** تعديل `Cookie.Path` من `OnSigningIn` وحده ليس حلاً مكتملاً لمسارات Path-Based. قد يعمل أول Login ثم يعيد Sliding Renewal إصدار Cookie بمسار مختلف، كما يجب أن يطابق Sign-Out نطاق الإصدار. يجب أن يثبت Spike التجديد والحذف باستخدام API extension points مدعومة، أو يرفض هذا النهج إذا تطلب تجاوزات هشة.

لا تُنشأ Schemes بعدد المتاجر كحل افتراضي؛ عدد المتاجر ديناميكي، وتسجيل Authentication Scheme لكل متجر ليس تصميماً تشغيلياً مناسباً دون مبرر واضح.

## 8. قرار مقترح — Proposed Decision

### 8.1 ما نعتمده الآن

1. الإبقاء على Path-Based Store URLs في MVP كما هو موثق.
2. تفضيل ASP.NET Core Cookie Authentication لتطبيق Browser SPA، باعتباره المرشح الأول للاختبار، لا قراراً إنتاجياً نهائياً.
3. تفضيل نشر React وAPI على Same Origin في MVP عبر Reverse Proxy أو Host واحد.
4. عدم استخدام Identity proprietary Bearer Tokens على أنها JWT.
5. عدم إصدار Standard JWT إلا إذا أثبتت المتطلبات وجود مستهلكين إضافيين أو سبب تشغيلي/أمني يبرر Token Server.
6. إبقاء `StoreCustomer` و`BackOffice` ضمن حدود مصادقة منفصلة من حيث نوع الحساب والسياسات، وعدم استخدام جلسة Customer لإثبات صلاحية BackOffice.

### 8.2 ما لا نعتمده بعد

- لا نعتمد Cookie واحدة عامة لكل المتاجر مع افتراض أنها تدعم حسابات متزامنة مستقلة.
- لا نعتمد `Cookie.Path` كآلية Tenant Security أو كبديل عن Authorization.
- لا نعتمد Cookies بنفس الاسم على Paths متعددة قبل إثبات إدارة الإصدار والتجديد والحذف، ومراجعة مخاطره وفق OWASP.
- لا نعتمد BFF أو Redis/Distributed Session Store قبل إثبات أن متطلبات الجلسات تحتاجهما.
- لا نعتمد JWT داخل `localStorage` أو `sessionStorage`.
- لا نغير URL Topology إلى Subdomains دون قرار منتج منفصل يوازن العزل والتشغيل.

### 8.3 بوابة القرار النهائي

يصبح ADR `Accepted` فقط عندما:

- يُحسم معنى تعدد المتاجر في المتصفح: تبديل حساب، أم جلسات مستقلة متزامنة.
- يُحسم Origin الخاص بواجهة React والـ API في بيئة MVP.
- ينجح Spike في إصدار الجلسة وتحققها وتجديدها وحذفها وإبطالها وفق السياسة.
- لا توجد حالة Cross-Store Session Leakage.
- تمر اختبارات CSRF وOrigin/CORS ذات الصلة.
- يثبت الأداء المقبول مقارنة بخط أساس بلا Authentication.

## 9. ضوابط التصميم المطلوبة بغض النظر عن البديل

### 9.1 ترتيب HTTP Pipeline

مع المحافظة على Scope الحالي، يكون الترتيب المنطقي:

```csharp
app.UseRouting();
app.UseCors(/* exact policy only when cross-origin is required */);
app.UseMiddleware<IdentityScopeMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

- يجب وضع CORS في الموضع الذي توصي به Microsoft بين Routing وEndpoints عند الحاجة.
- يجب أن يسبق `IdentityScopeMiddleware` المصادقة في RetailFlow؛ حتى يكون Store/BackOffice Scope معروفاً قبل أي Authentication operation قد تحتاج UserStore.
- لا يعتبر هذا المقتطف موافقة على إضافة الـ Middleware الآن؛ ترتيب التنفيذ النهائي يراجع مع CORS/Antiforgery والـ endpoint mapping.

### 9.2 Tenant Binding

على كل طلب StoreCustomer محمي:

```text
Route Slug
   ↓
Resolve active Store
   ↓
Establish IdentityLookupScope(StoreCustomer, StoreId)
   ↓
Authenticate session
   ↓
Verify AccountKind == StoreCustomer
   ↓
Verify authenticated StoreId == resolved StoreId
   ↓
Authorize endpoint
   ↓
Execute Store-scoped application operation
```

- لا يعتمد النظام على وجود Claim وحده دون مقارنته بالسياق المحسوم للخادم.
- غياب Scope أو فشل Resolver يجب أن يفشل مغلقاً.
- BackOffice يصل إلى بيانات متجر فقط بعد فحص Membership وصلاحيات العملية.

### 9.3 Cookie baseline إذا نجح البديل A

هذه قيم ابتدائية للـ Spike وليست تكوين إنتاجياً نهائياً:

| الخاصية | القيمة التجريبية | ملاحظة |
|---|---|---|
| `Secure` | Always | HTTPS فقط |
| `HttpOnly` | true | لا يقرأ JavaScript Cookie المصادقة |
| `Domain` | غير محدد | Host-only Cookie |
| `SameSite` | Lax للـ Same-Origin baseline | تعاد المراجعة عند اختلاف Origin/Site |
| `Path` | لا يخصص لكل متجر قبل Spike | لا يُستخدم كحد أمني؛ يجب اختبار دورة الحياة كاملة |
| `IsPersistent` | false افتراضياً | Remember Me قرار منتج مستقل وموافقة صريحة |
| `ExpireTimeSpan` | يحدد بعد سياسة Session Lifetime | لا يُختار رقم عشوائي |
| `SlidingExpiration` | يجب اختباره مع Cookie scope | التجديد يجب ألا يوسع Path أو يغيره |

ملاحظة: `__Host-` Cookie Prefix يشترط `Path=/`، لذا لا يتوافق مع محاولة Path-scoped cookies. لا تستخدم هذا الـ Prefix مع Path مخصص؛ إذا استُخدم Host-only `Path=/` تحت Subdomain فحينها يمكن تقييمه وفق شروطه.

### 9.4 CSRF وCORS (القرار المعتمد لـ Same-Origin Browser API)

- في نموذج **Same-Origin Browser API**، يعمل React SPA وASP.NET Core API على نفس الـ Origin دون CORS (`No CORS`)، مع إرسال `credentials: "same-origin"`.
- لا يُعتمد على `SameSite` وحده؛ وتُستبدل آلية Synchronizer Antiforgery Token في واجهة الـ API بـ **`BrowserCsrfProtectionMiddleware`** التي تجمع ثلاث طبقات متكاملة للطلبات المغيّرة للحالة (`POST`, `PUT`, `PATCH`, `DELETE`، وكل طلب غير `GET/HEAD/OPTIONS/TRACE`)، بما فيها نقاط ما قبل المصادقة مثل `Login`:
  1. **Custom Request Header (`X-RetailFlow-Request: 1`):** ليس سراً ولا Token مصادقة، بل يجبر المتصفح على إرسال CORS Preflight عند أي محاولة Cross-Origin. يجب أن يرسله React API Client المركزي في كل طلب يغيّر الحالة.
  2. **Fetch Metadata (`Sec-Fetch-Site`):** عند وجوده يُقبل `same-origin` فقط، ويُرفض `same-site` و`cross-site` و`none`. عند غيابه يُعتمد على فحص Origin/Referer.
  3. **Strict Origin / Referer Validation:** مقارنة دقيقة (`Scheme + Host + Port`) مقابل `Security:BrowserCsrf:BrowserOrigin` (إعداد إلزامي بنمط Fail-Fast عند الإقلاع دون قيمة افتراضية غير مؤكدة، يقبل `https://` فقط في `Staging` و`Production`، ويسمح بـ `http://` لـ Loopback في `Development` فقط)، مع رفض `Origin: null`، ومنع الرجوع إلى `Referer` إذا وُجد `Origin` غير صالح، ومنع الاعتماد على `Request.Host`.
- الطلبات المرفوضة تتوقف قبل الوصول إلى Endpoint وتعيد `403 Forbidden` مع `ProblemDetails` وترويسة `Cache-Control: no-store` وتسجيل سبب الرفض التشخيصي دون تسجيل Cookies أو Tokens.

#### عقد React API Client المطلوب (عند بناء الواجهة)

لا يوجد React Frontend في المستودع حالياً؛ وعند بنائه يجب أن يلتزم العميل المركزي بإرسال `X-RetailFlow-Request: 1` و`credentials: "same-origin"` في كل طلب يغيّر الحالة:

```typescript
await fetch("/api/stores/store-a/customers/profile", {
  method: "POST",
  credentials: "same-origin",
  headers: {
    "Content-Type": "application/json",
    "X-RetailFlow-Request": "1",
  },
  body: JSON.stringify(payload),
});
```

### 9.5 XSS وحدود Path

Cookie `HttpOnly` تمنع قراءة قيمة Cookie عبر `document.cookie`، لكنها لا تمنع سكريبت XSS من تنفيذ طلبات مصادق عليها في سياق المستخدم.

كما أن Cookie Path يحدد متى يرفق المتصفح Cookie؛ لكنه ليس حاجزاً أمنياً بين مسارات التطبيق على Host واحد. لذلك:

- لا تعتبر `/store-a` و`/store-b` عزل Origin.
- لا تعتمد على Path لإثبات Tenant Isolation.
- طبّق Output Encoding وتنظيف HTML المسموح وCSP صارمة قدر الإمكان.
- يجب أن تبقى فحوص StoreId وAuthorization داخل الخادم.

### 9.6 Session Lifecycle

يجب توثيق واعتماد سياسة منفصلة لكل من:

- Idle Timeout.
- Absolute Session Lifetime.
- Sliding Renewal.
- Logout من الجهاز/المتصفح الحالي.
- Sign-out everywhere.
- تغيير كلمة المرور أو البريد.
- تعطيل الحساب أو المتجر.
- إلغاء StoreMembership في BackOffice.

`SecurityStampValidatorOptions.ValidationInterval` افتراضياً 30 دقيقة في ASP.NET Core Identity. تقليل الفاصل يسرع التقاط تغييرات Security Stamp لكنه يزيد تكرار التحقق من مخزن البيانات؛ ويجب قياس المفاضلة وعدم وصف Security Stamp بأنه إبطال فوري.

لا يضاف `ITicketStore` أو Distributed Session Store إلا إذا كانت متطلبات الإبطال الفوري أو إدارة الأجهزة تبرر lookup خادمياً إضافياً لكل مصادقة.

## 10. Session Transport Spike — خطة الإثبات

### 10.1 نطاق التجربة

- مشروع/فرع Spike معزول عن Identity Store المثبت.
- متجران تجريبيان على الأقل، وحساب `StoreCustomer` لكل متجر له نفس البريد وكلمة مرور مستقلة.
- نفس React SPA ونفس API contracts قدر الإمكان.
- SQL Server فعلي لاختبارات Identity المرتبطة بالمخزن.
- Browser automation لاختبار Cookie Jar وسلوك Tabs والـ Requests الفعلية.
- لا ينفذ Catalog أو Orders أو Checkout؛ التجربة محصورة في المصادقة والجلسات.

### 10.2 Test Matrix

| ID | السيناريو | معيار القبول |
|---|---|---|
| ST-01 | Login إلى Store A | نجاح scoped lookup وإصدار جلسة A |
| ST-02 | Login إلى Store B بالبريد نفسه | لا يحدث خلط لحساب A، وتصدر جلسة B مستقلة إذا كان التزامن مطلوباً |
| ST-03 | طلب محمي إلى Store A | لا تُستخدم هوية B في A |
| ST-04 | طلب محمي إلى Store B | لا تُستخدم هوية A في B |
| ST-05 | تبديل Tabs بين A وB | كل Tab/طلب يتبع حساب المتجر المقصود |
| ST-06 | Logout من A | لا تنتهي جلسة B عند اعتماد الجلسات المتزامنة |
| ST-07 | Cookie Renewal | لا يتغير Name/Path/Domain ولا تستبدل جلسة متجر آخر |
| ST-08 | Concurrent requests | لا يحدث race في تجديد/حذف/استبدال Ticket |
| ST-09 | Cross-store replay | إعادة إرسال جلسة A إلى مسار B تُرفض |
| ST-10 | AccountKind mismatch | Customer لا يدخل BackOffice والعكس |
| ST-11 | CSRF | الطلب المغيّر للحالة من Origin غير موثوق يُرفض |
| ST-12 | CORS | لا توجد Origins واسعة أو Credentials غير مضبوطة |
| ST-13 | XSS threat model | لا يمكن قراءة Cookie عبر JS؛ ويوثق أن XSS لا يزال قادراً على إرسال طلبات في سياق المستخدم |
| ST-14 | Password change / Security Stamp | السلوك ومدة التقاط الإبطال مطابقة للسياسة المعتمدة |
| ST-15 | API restart | الجلسة تتصرف وفق Key Ring وLifetime policy، دون فقد غير مبرر |
| ST-16 | Two API instances | كلا النسختين تتحققان من نفس الجلسة/الـ protected tokens عند استخدام Cookie |
| ST-17 | Session expiration | Idle وAbsolute expiration يعملان كما هو موثق |
| ST-18 | Login failure | رسائل عامة، Lockout يعمل، ولا يوجد User Enumeration |
| ST-19 | BackOffice membership | تغيير/إلغاء Membership يمنع الوصول إلى متجر غير مصرح |
| ST-20 | Store slug case/canonicalization | لا يؤدي اختلاف حالة الأحرف أو canonical slug إلى ربط Cookie بمتجر غير مقصود |

### 10.3 مقارنة الأداء

تُقاس البدائل على نفس الجهاز/البيئة، ونفس API endpoint، ونفس قاعدة البيانات، وبنفس حمل الطلبات:

- Warm-up قبل القياس.
- 1 و10 و50 و100 concurrent clients (أو مستويات مناسبة للبيئة المتاحة).
- تسجيل P50/P95/P99 للزمن، Requests/sec، CPU، Allocated Bytes، وSQL command count.
- مقارنة unauthenticated baseline وauthenticated success و401/403 وSecurity Stamp revalidation.
- فصل تكلفة التحقق المحلي من Ticket/JWT عن تكلفة أي database/session-store lookup.
- قياس Login وRefresh/Renewal وLogout بشكل منفصل عن read endpoint.
- تسجيل حجم Cookie/Authorization Header وعدد Cookies المرسلة على الطلب.

لا تُعتمد أرقام أداء نظرية من الإنترنت كبديل عن قياس RetailFlow. تُحدد حدود الأداء بعد ظهور Baseline ومتطلبات الاستضافة، ويجب ألا يكون البديل المختار أبطأ أو أكثر استهلاكاً بصورة غير مبررة.

### 10.4 ما تم إثباته في SessionTransportSpike حتى الآن (`2026-11-25`)

- **`StoreCustomerCookieManager`:** يصدر Cookie باسم خاص بكل متجر (`__Secure-RetailFlow.StoreCustomer.store.{storeId:N}`) ومسار متجر مطبّع بالأحرف الصغيرة (`/api/stores/{slug}/customers`) مع `Secure` و`HttpOnly` و`SameSite=Lax`، مع اختبارات وحدة (`7/7`).
- **`CanonicalStoreSlugMiddleware`:** يرفض المسارات غير القياسية للـ slug بـ `400 Bad Request` (`ProblemDetails`) قبل `BrowserCsrfProtectionMiddleware` و`IdentityScopeMiddleware`، مع اختبارات وحدة (`5/5`).
- **`BrowserCsrfProtectionMiddleware` و`BrowserCsrfProtectionOptions`:**
  - استبدال آلية Synchronizer Antiforgery Token القديمة (`AddAntiforgery`, `X-CSRF-TOKEN`, `GET /antiforgery`) بالكامل بحماية `X-RetailFlow-Request: 1` + `Sec-Fetch-Site` + Strict `Origin`/`Referer` مقابل `Security:BrowserCsrf:BrowserOrigin` (Fail-Fast عند الإقلاع عبر `BrowserCsrfProtectionOptionsValidator` و`IHostEnvironment`، مع قصر `http://` Loopback على `Development` فقط).
  - حماية `Login` (قبل المصادقة) و`Logout` وجميع الطرق غير الآمنة، مع إرجاع `403 Forbidden` و`ProblemDetails` و`Cache-Control: no-store`، واختبارات وحدة (`45` حالة في `BrowserCsrfProtectionMiddlewareTests` و`BrowserCsrfProtectionOptionsValidatorTests`).
- **`ITicketStore` / Per-Session Server-side Revocation (`SqlServerTicketStore` و`SessionTicketDbContext`):**
  - إنشاء `PersistedAuthenticationTicket` و`SessionTicketDbContext` (`AuthenticationSessionTickets`) و`SessionTicketDbContextFactory` (`__EFMigrationsHistory_SessionTickets`) وتطبيق Migration `20261124225416_CreateAuthenticationSessionTickets` على قاعدة التطوير المحلية `RetailFlowIdentitySpike` (دون المساس بجداول Identity).
  - تنفيذ `SqlServerTicketStore` (مفتاح جلسة عشوائي `32 bytes` Base64Url بطول `43` محرفاً، حماية `AuthenticationTicket` عبر `IDataProtector`، التحقق من `StoreCustomer` metadata عند التخزين والاسترجاع والتجديد، حقن `TimeProvider` وتمريره إلى `CookieAuthenticationOptions.TimeProvider` لتطابق الساعة بين Handler وStore، وفرض تجديد رتيب `existing.ExpiresUtc > now && existing.ExpiresUtc < expiresUtc` يمنع `Stale Renewal` كما يمنع `RenewAsync` من إعادة إنشاء جلسة محذوفة أو تغيير هويتها).
  - تنفيذ `ExpiredSessionTicketCleanupService` (`BackgroundService` يعتمد على `PeriodicTimer` كل 5 دقائق و`TimeProvider` و`ExecuteDeleteAsync` لإزالة الجلسات المنتهية من `AuthenticationSessionTickets` دورياً).
  - إضافة حارس `409 Conflict` في `POST /login` لمنع تبديل الحساب داخل جلسة نشطة قبل `Logout`.
- **`StoreCustomerCookieAuthenticationEvents` و`StoreCustomerClaimsPrincipalFactory`:** يربطان الجلسة بـ `AccountKind` و`StoreId`، ويعيدان `401 Unauthorized` / `403 Forbidden` (بدل `302` redirect) للـ SPA/API، ويرفضان إعادة استخدام Ticket متجر A على مسار متجر B مع حذف Cookie النطاق الحالي فقط (`DeleteCurrentScopeCookie` بدل `SignOutAsync`) حتى لا تُبطَل جلسة Store A على الخادم.
- **`Shared Data Protection Key Ring` (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 10.0.15`):**
  - توسيع `SessionTicketDbContext` لتنفيذ `IDataProtectionKeyContext` (`DataProtectionKeys`) وتطبيق Migration `20261125063008_AddDataProtectionKeys` (`AddDataProtectionKeys.sql`) على قاعدة التطوير المحلية `RetailFlowIdentitySpike` دون تعديل `AuthenticationSessionTickets` أو جداول Identity.
  - تثبيت `ApplicationName = "RetailFlow.Auth"` (`RetailFlowDataProtectionDefaults.ApplicationName`) وحماية مفاتيح Key Ring بشهادة X.509 عبر `DataProtectionCertificateLoader` (`X509CertificateLoader.LoadPkcs12FromFile` مع `EphemeralKeySet`، و`PersistKeysToDbContext<SessionTicketDbContext>()`، و`ProtectKeysWithCertificate`، و`UnprotectKeysWithAnyCertificate`).
- **ضوابط إضافية:** ضبط `SecurityStampValidatorOptions.ValidationInterval = 5m` (أقل من `ExpireTimeSpan = 15m`)، وموازنة زمن `POST /login` بـ Dummy Hash عند عدم وجود المستخدم.
- **اختبارات التكامل على SQL Server (`12/12` Passed، 0 تحذيرات، عبر `WebApplicationFactory` و`FakeTimeProvider` عبر `Microsoft.Extensions.TimeProvider.Testing 10.0.0`):**
  - `Login_Me_Logout_RemovesBrowserSessionCookie`: يثبت `Login (204)` -> `Me (200)` -> رفض `Logout` دون `X-RetailFlow-Request` (`403`) -> رفض `Logout` من Origin خارجي (`403`) -> `Logout` صحيح (`204 + حذف Cookie + حذف سجل SQL`) -> إعادة إرسال Cookie الأصلية نفسها بعد `Logout` تعيد **`401 Unauthorized`** (إبطال خادمي فعلي).
  - `StoreACookie_CannotAuthenticateAgainstStoreB`: يثبت رفض Cookie متجر A على متجر B ورفض Ticket A المنسوخ باسم Cookie متجر B (`401 Unauthorized` مع حذف Cookie B)، مع بقاء جلسة Store A صالحة (`200 OK`).
  - `CsrfProtection_EnforcesCustomHeader_OriginReferer_AndFetchMetadata`: يثبت رفض `Login` (بـ `403` ودون إصدار Cookie) عند غياب Header أو Origin خارجي أو `Origin: null` أو غياب Origin وReferer أو `Sec-Fetch-Site` بقيم `cross-site`/`same-site`/`none` أو Origin غير صالح مع Referer صالح، ويثبت قبول الطلب الصحيح عند غياب `Sec-Fetch-Site` أو عند غياب `Origin` مع `Referer` صالح.
  - `SlidingRenewal_UpdatesExistingSessionWithoutCreatingAnother`: يقدّم `FakeTimeProvider` بمقدار 8 دقائق (بعد منتصف عمر `15m`) ثم يرسل `GET /me`، ويثبت إصدار Cookie مجددة وتحديث السجل نفسه في SQL Server (`Key` ثابت، تمديد `ExpiresUtc` و`LastRenewedUtc`، عدد السجلات `1`) ونجاح المصادقة بالـ Cookie المجددة.
  - `RenewAsync_WhenStaleRenewalArrivesLater_DoesNotOverwriteNewerSession`: ينفذ تجديداً أحدث (`+30m`) ثم تجديداً متأخراً أقدم (`+20m`)، ويثبت بقاء `ExpiresUtc` و`ProtectedTicket` و`LastRenewedUtc` و`RetrieveAsync` على القيمة الأحدث.
  - `RenewAndRemove_WhenExecutedConcurrently_KeepSessionRevoked`: يشغّل `RenewAsync` و`RemoveAsync` بالتوازي عبر `Task.WhenAll` لمدة 20 تكراراً، ويثبت بقاء الجلسة محذوفة (`Count = 0` و`RetrieveAsync == null` و`GET /me -> 401`) دون إحيائها.
  - `Login_WhenAnotherSessionIsAlreadyAuthenticated_ReturnsConflict`: يثبت رفض تسجيل الدخول لحساب ثانٍ أثناء وجود جلسة نشطة بالـ `409 Conflict` (`ProblemDetails`) دون إصدار `Set-Cookie` جديد مع بقاء الجلسة الأولى فعالة (`200 OK`).
  - `CleanupExpiredTickets_RemovesOnlyExpiredSessions`: يثبت حذف الجلسة المنتهية فقط من SQL Server وإرجاع `401 Unauthorized` عند إعادة إرسال الـ Cookie الخاصة بها، مع بقاء جلسة المتجر النشط وسجلها سليمين.
  - `CleanupExpiredTickets_WhenAllSessionsAreActive_DeletesNothing`: يثبت أن دورة التنظيف لا تحذف أي سجل عندما تكون الجلسات نشطة (`DeletedCount = 0`).
  - `InstanceA_CookieAndTicket_AreReadableByInstanceB_AndAfterRestart`: يثبت على ثلاث مراحل (`Instance A` -> `Instance B` مستقل -> `Instance C` جديد بعد إيقاف `B`) استرجاع الـ Cookie والتذكرة المحمية من SQL Server والتحقق من `<encryptedSecret>` في `DataProtectionKeys`.
  - `DifferentApplicationName_CannotUnprotectCookieOrTicket`: يثبت فشل فك التذكرة والـ Cookie (`RetrieveAsync == null` و`401 Unauthorized`) عند اختلاف `ApplicationName`.
  - `WrongCertificate_CannotUnprotectCookieOrTicket`: يثبت فشل فك التذكرة والـ Cookie (`RetrieveAsync == null` و`401 Unauthorized`) عند استخدام شهادة X.509 مختلفة.
  - **المتبقي لإغلاق بوابات ADR:** اختبار انتهاء الجلسة الصريح بعد `ExpireTimeSpan`، و`403 Authorization Policy`، واختبارات المتصفح بـ Playwright (قسم 10.5)، وقياس الأداء (قسم 10.3)، إضافةً إلى متطلبات ما قبل الإنتاج الخاصة بـ Data Protection (`Certificate Rotation`، و`Backup/Restore` لملف PFX، واختبار عمليتي OS مستقلتين، ومراقبة مزامنة Key Ring).

### 10.5 اختبارات المتصفح المطلوبة لاحقاً (Browser-Level Acceptance)

لا توجد بنية Playwright أو اختبارات متصفح حقيقية في المستودع حتى الآن؛ واختبارات `WebApplicationFactory` (`HttpClient`) لا تغني عن اختبار Cookie Jar الفعلي وتعدد التبويبات في المتصفح. الاختبارات المطلوبة قبل إغلاق ADR بالكامل:
- تسجيل الدخول إلى Store A وStore B في تبويبين ضمن المتصفح نفسه.
- نجاح طلبات كل تبويب على متجره دون تداخل جلسات.
- رفض Cross-Site POST من صفحة خارجية فعلية.
- رفض Login CSRF قبل إصدار أي Cookie في المتصفح.
- Logout من متجر A لا يؤدي إلى حذف Cookie متجر B.

## 11. معايير قبول ADR

لا يُغلق `AUTH-ADR-01` إلا بعد تحقق كل ما يلي:

- [ ] حُسمت دلالة تعدد المتاجر في المتصفح (switching أم simultaneous sessions).
- [ ] حُددت علاقة Origin/Host بين React وAPI في MVP.
- [ ] تم اختيار بديل واحد بناءً على نتائج Spike، لا التفضيل الشخصي.
- [ ] نجاح Cross-Store isolation لكل من Store A وStore B.
- [ ] نجاح Login/renewal/logout/expiration، بما فيها اختبارات التزامن (تمت تغطية Login وSliding Renewal وStale Renewal وتزامن Renew/Remove وLogout في `SessionTransportSpike`؛ متبقي اختبار انتهاء الجلسة الصريح بعد `ExpireTimeSpan`).
- [x] وجود CSRF/CORS policy صريحة ومختبرة بحسب نموذج النشر (`Same-Origin` + `No CORS` + `BrowserCsrfProtectionMiddleware` مختبرة في `SessionTransportSpike`؛ وبقي تأكيدها عبر متصفح Playwright في 10.5).
- [ ] توثيق سياسة Security Stamp والإبطال وتغيير كلمة المرور.
- [x] اختبار تعدد API Instances وData Protection أو Signing Keys (مختبر في `SessionTransportSpike` عبر SQL Server Key Ring و`DataProtectionKeys` وحماية X.509 و`ApplicationName = RetailFlow.Auth` واختبارات `A/B/C` و`DifferentApplicationName` و`WrongCertificate`؛ متبقي قبل الإنتاج: `Certificate Rotation` و`Backup/Restore` لملف PFX واختبار عمليتي OS مستقلتين).
- [ ] تقرير أداء قابل لإعادة الإنتاج مع إعدادات الاختبار ونتائجه.
- [ ] مراجعة Source/API contracts وThreat Model قبل إضافة Authentication إلى `Program.cs`.

## 12. المخاطر والقرارات التابعة

| الخطر/المسألة | الأثر | المعالجة |
|---|---|---|
| افتراض أن Cookie Path يعزل المستأجرين | حرج | لا يُستخدم كحد أمني؛ تحقق Scope/Claims/Authorization إلزامي |
| تجديد Cookie بمسار غير مسار إصدارها | مرتفع | اختبار Sliding Renewal وLogout؛ لا يكفي OnSigningIn وحده |
| اختلاف Origin بين React وAPI | مرتفع | Same-Origin deployment مفضل؛ أو CORS/credentials/CSRF مضبوط ومختبر |
| عدم حسم concurrent sessions | مرتفع | اختبار ST-02 إلى ST-06؛ وقد يلزم Subdomain أو BFF |
| JWT بدون Refresh Token Security | مرتفع | لا يعتمد JWT حتى اكتمال Rotation/Reuse Detection/Revocation |
| Key Ring مؤقت أو غير مشترك | مرتفع | تخزين دائم ومشترك ومقيد الصلاحيات مع تشفير مناسب |
| إبطال Security Stamp غير فوري | متوسط/مرتفع | تحديد Validation Interval وقياس كلفته وتوثيق نافذة الإبطال |
| Cross-tenant XSS في Shared Origin | حرج | منع XSS، CSP، وعدم اعتبار المسار حاجز Origin |

## 13. النتائج المتوقعة لكل قرار

### إذا قُبل Cookie Authentication

- استخدام ASP.NET Core Cookie handler مع تكامل SignInManager المناسب.
- تصميم واضح للكوكيز بحسب StoreCustomer وBackOffice.
- حماية CSRF إلزامية للطلبات المغيّرة للحالة (عبر `BrowserCsrfProtectionMiddleware`: Custom Request Header + Strict Origin/Referer + Fetch Metadata دون CORS في نموذج Same-Origin).
- Data Protection Key Ring دائم ومشترك في بيئة تعدد النسخ.
- سياسة انتهاء وتجديد وإبطال موثقة.
- لا يُسمح بتخصيص Cookie path per store إلا إذا اجتازت اختبارات دورة الحياة والأمن كاملة.

### إذا قُبل Standard JWT

- تصميم Issuer/Audience/Signing Key/Algorithm/Key Rotation.
- Access Token قصير العمر، وRefresh Token opaque ومحفوظ كـ verifier/hash في التخزين الخادمي.
- Rotation مع كشف إعادة الاستخدام وإبطال عائلة الرموز وفق RFC 9700 عند بناء OAuth-style public client flow.
- عدم وضع Tokens في Web Storage.
- تحديد استراتيجية Browser Token Storage؛ وقد يتطلب الأمر BFF لتجنب تعريض أسرار طويلة العمر إلى JavaScript.
- اختبار إبطال الجلسة بعد تغيير كلمة المرور/تعطيل الحساب، وعدم اعتبار انتهاء JWT وحده Logout فورياً.

## 14. خطة التنفيذ بعد قبول ADR

1. تثبيت نتائج Session Transport Spike وقرار URL/Origin.
2. تحديث هذا ADR إلى `Accepted` مع Commit SHA لتقرير الاختبار.
3. تحديث `phase-06-identity-spike-status.md` مع نتائج الاختبارات وأرقام الأداء وحدود الإثبات.
4. تنفيذ Authentication registration والـ Pipeline في IdentitySpike فقط.
5. تنفيذ Minimal Auth Endpoints المخصصة وفق Scoped `UserManager` و`SignInManager`، وعدم اعتماد `MapIdentityApi` كما هو.
6. إضافة Integration Tests على SQL Server وBrowser-level tests قبل نقل القرار إلى المنتج الأساسي.
7. مراجعة CI وData Protection/Signing key deployment قبل اعتبار الميزة قابلة للنشر.

## 15. المراجع

### وثائق RetailFlow

- `RetailFlow_Complete_Documentation_AR_Phases_01-06.pdf`: Product Vision وMVP URL pattern وTechnology Stack، خصوصاً صفحات 3 و10.
- `06_RetailFlow_Identity_PoC_Authentication_Study_AR.pdf`: Identity Scope، Cookie/JWT comparison، Path/Subdomain/Custom Domain، Data Protection، Acceptance Gate، خصوصاً الصفحات 4 و13–19.
- `phase-06-identity-spike-status.md`: حالة Identity Spike والاختبارات الحالية.

### Microsoft — ASP.NET Core 10

- [Use Identity to secure a Web API backend for SPAs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0) — Cookies الموصى بها لـ Browser SPA، Identity proprietary tokens، وطلب `credentials`.
- [Cookie Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) — إعداد Cookie Authentication، API 401/403 behavior في ASP.NET Core 10، وتكوين Lifetime.
- [Prevent CSRF attacks](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) — Antiforgery للـ SPA.
- [Enable CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0) — Origins وCredentials وترتيب Middleware.
- [Configure Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0) — Key Ring persistence/protection للمضيفين المتعددين.
- [SecurityStampValidatorOptions.ValidationInterval](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.securitystampvalidatoroptions.validationinterval?view=aspnetcore-10.0) — افتراضي 30 دقيقة ومفاضلة freshness مقابل datastore traffic.
- [ASP.NET Core CookieAuthenticationHandler — release/10.0 source](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs) — مسارات SigningIn وSliding Renewal وSigningOut.
- [CookieAuthenticationOptions — release/10.0 source](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Security/Authentication/Cookies/src/CookieAuthenticationOptions.cs) — إعدادات Cookie وSessionStore وExpiration.

### OWASP / IETF / Browser Platform

- [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html) — Secure/HttpOnly/SameSite، Path limitations، session lifecycle، storage warnings.
- [OWASP CSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html) — CSRF Token وOrigin validation.
- [OWASP Multi-Tenant Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html) — Tenant context and isolation.
- [RFC 7519 — JSON Web Token](https://www.rfc-editor.org/rfc/rfc7519) — JWT format and claims.
- [RFC 9700 — OAuth 2.0 Security Best Current Practice](https://www.rfc-editor.org/rfc/rfc9700) — Refresh Token rotation/reuse detection and token privilege restriction.
- [MDN — Set-Cookie](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Set-Cookie) — Browser Cookie Path/Domain/SameSite behavior; Path is not a security boundary.

---

## سجل التغييرات

| الإصدار | التاريخ | التغيير |
|---|---|---|
| 0.1 | 2026-11-24 | إنشاء مسودة القرار استناداً إلى Product Blueprint وPhase 06 ودراسة مصادر .NET 10 وOWASP/IETF. |
| 0.2 | 2026-11-24 | توثيق نتائج المرحلة الأولية من `SessionTransportSpike` (`14/14`) وإغلاق فجوات `ScopedUserStore` (`49/49` في `IdentitySpike`). |
| 0.3 | 2026-11-24 | مطابقة سلوك API مع توثيق ASP.NET Core 10 (`401/403`، `Antiforgery` لـ `Logout`، `ValidationInterval = 5m`، موازنة زمن `Login`، وRequest-Scoped Cache في `ScopedUserStore`). |
| 0.4 | 2026-11-25 | اعتماد حماية CSRF عبر `BrowserCsrfProtectionMiddleware` (`X-RetailFlow-Request: 1` + Strict `Origin/Referer` + `Sec-Fetch-Site` دون CORS) وإزالة Synchronizer Antiforgery Token، وتقييد `http://` Loopback بـ `Development` (`60/60` في `SessionTransportSpike`)، وإنشاء قاعدة تخزين `AuthenticationSessionTickets` وMigration الخاصة بها كخطوة أولى لـ `Per-Session Revocation`. |
| 0.5 | 2026-11-25 | تنفيذ `SqlServerTicketStore` (`ITicketStore`) وربطه في `Program.cs`، وتطبيق Migration محلياً على `RetailFlowIdentitySpike`، ومنع تبديل الحساب دون Logout (`409`)، وإثبات رفض إعادة استخدام Cookie الأصلية بعد Logout (`401`) مع بقاء جلسة Store A (`60/60` في `SessionTransportSpike`). |
