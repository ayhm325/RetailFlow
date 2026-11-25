---
trigger: model_decision
description: Apply to SQL Server, EF Core, schema, migrations, tenant isolation, data ownership, constraints, indexes, transactions, or persistence changes.
---
# Database and tenant isolation
- Treat tenant isolation as a server-enforced security boundary, not a UI, slug, route, cookie-path, or CORS convention.
- Derive tenant identity from trusted server-side resolution; never trust caller-supplied StoreId as authorization evidence.
- Review composite foreign keys, unique indexes, check constraints, row-version/concurrency behavior, query filters/projections, and cross-tenant negative tests.
- Design transactions around the full business invariant. For idempotency, verify atomic claim, unique constraint, duplicate/concurrent retry behavior, and replay semantics.
- Separate current schema from target schema. Derive current ERD from migrations/schema evidence, not desired diagrams.
- Never run destructive migration or data rewrite without explicit owner approval, backup/restore plan, and tested recovery path.
