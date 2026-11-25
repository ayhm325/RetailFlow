---
name: documentation-audit
description: Audit RetailFlow documentation against actual repository implementation and official technical references, preserving evidence and identifying contradictions and gaps.
---
# Documentation-to-code audit

1. Record the exact repository, branch, commit SHA, date, and scope.
2. Inventory source docs, baseline chapters, ADRs, registers, diagrams, API contracts, migrations, code, tests, and CI.
3. Establish actual implementation by inspecting source, configuration, migrations, and tests. Distinguish Product Host from experiments/PoCs.
4. Verify technical claims against current official documentation and standards; cite URLs, versions, and access dates. Use expert/community sources only as supporting context, not as a substitute for normative requirements.
5. Create findings with stable ID, severity, area, exact location, evidence, impact, recommendation, owner/approval need, closure criteria, and gate.
6. Build requirement-to-UC/BR/AC/test/source/commit/CI traceability. Never fill missing evidence by inference.
7. Review diagrams for Current vs Target accuracy, trust boundaries, state separation, transaction boundaries, and failure paths.
8. Report audit limitations honestly: files not inspected, tests not run, production/operational checks not performed.
9. Update `audit/` and control files only after preserving original source reports.
