---
name: traceability-maintenance
description: Maintain requirement, business rule, acceptance criteria, test, code, commit, CI and evidence links without losing stable identifiers.
---
# Traceability maintenance

For each affected FR/NFR/BR/UC/AC:
- preserve the existing stable identifier;
- link related requirements and use cases;
- link applicable business rules/invariants;
- define observable, testable acceptance criteria;
- identify concrete test project and test name/ID;
- identify exact implementation path(s), migration/constraint, or state "not implemented";
- record commit SHA and CI run URL when available;
- record status, evidence date, and deviations;
- distinguish not applicable from missing evidence.

Before closing:
- detect duplicate IDs and conflicting descriptions;
- check orphan ACs, BRs, tests, and diagrams;
- check that chapter prose agrees with canonical registers;
- leave unknowns explicit.
