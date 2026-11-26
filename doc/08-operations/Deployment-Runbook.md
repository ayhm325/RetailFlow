# Runbook النشر — Product Host

**الحالة:** Deployment design incomplete — لا توجد deployment manifests في baseline.

## 1. ما نعرفه

- تطبيق ASP.NET Core `net10.0`.
- SQL Server عبر EF Core.
- CI يبني على Ubuntu 24.04 ويستخدم SQL Server 2022 container للاختبارات.
- Production host pipeline يشغل forwarded headers, exception handler, HSTS, HTTPS redirection, security headers, rate limiter, authentication/authorization.
- Production requires explicit `AllowedHosts` وconnection string.

## 2. ما لا نعرفه من المستودع

لا توجد وثيقة أو manifest يثبت Target hosting platform، domain/TLS termination، reverse proxy، database hosting/HA، secrets manager، Data Protection persistence، scale-out topology، deployment slots، backup/restore أو rollback process.

## 3. Release checklist أولي

- [ ] تثبيت commit المراد نشره ونتيجة CI عليه.
- [ ] بناء Release artifact من نفس commit.
- [ ] مراجعة migrations/SQL script ومخاطر data migration.
- [ ] أخذ backup صالح واختبار restore.
- [ ] ضبط connection string من secret store.
- [ ] ضبط `AllowedHosts` بقيم hosts دقيقة.
- [ ] ضبط HTTPS/TLS وforwarded headers وفق proxy موثوق.
- [ ] تحديد Data Protection key ring persistence/share بين instances (`PersistKeysToDbContext`) وتزويد شهادة `DataProtection:CertificatePath` (مع `CertificatePassword` و`CertificateThumbprint`) لأن `RequireCertificateInProduction` يفرض الفشل المغلق افتراضياً (`true`)، واستخدام `PreviousCertificates` عند تدوير الشهادات.
- [ ] تحديد سياسة database access أقل صلاحية.
- [ ] التحقق من rate limits وtimeouts وrequest body limits.
- [ ] التحقق من health endpoints من deployment environment.
- [ ] تنفيذ smoke tests: login, store resolution, catalog, checkout test data, logout.
- [ ] رصد error rate وDB latency وsession behavior.
- [ ] توثيق rollback procedure والـ DB compatibility.

## 4. مبدأ النشر

لا تُطبق migrations تلقائياً في كل instance عند startup دون قرار هندسي. الأفضل وجود خطوة release واحدة مضبوطة، review للـ migration، backup، ثم deploy compatible application/schema مع خطة rollback. يلزم تصميم هذه العملية بما يناسب الاستضافة الفعلية قبل اعتمادها.

## 5. Forwarded Headers

الكود يضع `UseForwardedHeaders()` في أول pipeline ويذكر إعداد App Service Linux. لا تفعّل forwarded headers اعتماداً على أي مصدر غير موثوق؛ يجب تقييد الـ proxy trust boundary والتأكد أن التطبيق غير مكشوف مباشرة لمسار يسمح بانتحال X-Forwarded-*.
