# المعايير والمراجع الرسمية ومراجع الخبرة

تم اختيار هذه المراجع لأنها ترتبط مباشرة بالتقنيات أو أساليب التوثيق الموجودة في RetailFlow. الروابط أدناه هي مصادر إرشادية؛ يجب الرجوع للنسخة الحالية عند كل مراجعة، وعدم اعتبار الاستشهاد امتثالاً تلقائياً.

## Microsoft / .NET

1. **.NET releases and support** — دعم الإصدارات وسياسة LTS/STS: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
2. **Minimal APIs authentication and authorization (ASP.NET Core 10)**: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0
3. **CookieAuthenticationOptions.Cookie** — خصائص cookie, HttpOnly, SameSite, SecurePolicy: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.cookie
4. **EF Core Transactions** — SaveChanges transactions, explicit transactions and savepoints: https://learn.microsoft.com/en-us/ef/core/saving/transactions
5. **EF Core Handling Concurrency Conflicts** — optimistic concurrency and SQL Server rowversion: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
6. **EF Core Saving Data** — atomicity of a SaveChanges call on relational providers: https://learn.microsoft.com/en-us/ef/core/saving/

## OWASP

7. **OWASP ASVS 5.0.0** — Application Security Verification Standard; stable release published 30 May 2025: https://asvs.owasp.org/
8. **OWASP API Security Top 10 2023**: https://owasp.org/www-project-api-security/
9. **OWASP Password Storage Cheat Sheet** — adaptive password hashing and algorithm parameters: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
10. **OWASP Session Management Cheat Sheet** — cookie and server-side session practices: https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html
11. **OWASP CSRF Prevention Cheat Sheet** — Origin/Referer and token-based defenses: https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html

## Architecture documentation / experts

12. **C4 Model — Diagrams** — context, container, component, code, and supporting diagrams: https://c4model.com/diagrams
13. **C4 Model — Introduction** — describing an existing codebase at different levels: https://c4model.com/introduction
14. **UK Department for Education — Architecture documentation** — recommends lightweight ADRs including context, decision, consequences and source control: https://ddt.beta.education.gov.uk/guides/architecture-documentation
15. **Thoughtworks Enterprise Architecture Playbook** — lightweight ADR guidance and storing decisions in source control: https://www.thoughtworks.com/content/dam/thoughtworks/documents/report/thoughtworks-enterprise-architecture-playbook.pdf

## كيفية استخدام المراجع

- استخدم Microsoft Learn كمرجع للسلوك الرسمي للـ framework/provider.
- استخدم OWASP ASVS لتحديد متطلبات تحقق، وOWASP API Top 10 لتحديد فئات المخاطر؛ لا تستخدم Top 10 كبديل لقائمة ضوابط شاملة.
- استخدم Cheat Sheets لتصميم ومراجعة controls، مع توثيق إصدار/تاريخ المراجعة.
- استخدم C4 للمنظور المعماري، ولا تفرض كل مستويات C4 إذا لم تضف قيمة.
- استخدم ADRs لتسجيل القرارات المهمة مع بدائلها وآثارها؛ ADRs في هذه الحزمة استنتاجية لحين اعتمادها.
