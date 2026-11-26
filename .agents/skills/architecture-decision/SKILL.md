---
name: architecture-decision
description: Research and prepare an evidence-based Architecture Decision Record for a material RetailFlow decision without self-approving it.
---
# Architecture decision workflow

1. Define the decision question, scope, constraints, and why the current design is insufficient.
2. Read all related existing ADRs, requirements, security rules, implementation docs, and code.
3. Research current official vendor/framework documentation first; then use reputable engineering sources/standards for operational experience. Record URL, title, version/publication date, access date, and what the source supports.
4. Separate documented facts from assumptions, product preferences, and recommendations.
5. Compare at least two viable options using security, correctness, operability, performance, cost, complexity, migration, rollback, and lock-in.
6. Explain the selected recommendation and consequences; identify unresolved decisions and measurable validation criteria.
7. Create a Proposed ADR using `doc/02-architecture/adr/ADR-TEMPLATE.md`; update `doc/02-architecture/adr/README.md` and link related findings in `doc/10-open-items/Production-Gaps-and-Decision-Backlog.md`.
8. Do not mark Accepted or alter an existing accepted ADR. Ask the owner to approve.
