# AI Handoff — Start Here Next Session

> This is an initial handoff generated from the repository snapshot and audit package. It is not a record of work performed after that snapshot.

## Repository anchor
- Repository: `ayhm325/RetailFlow`
- Last observed branch: `main`
- Last observed HEAD: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`
- Snapshot date: `2026-11-26`
- First action: inspect Git status/branch/HEAD and confirm whether this SHA is still current.

## Active work
Documentation-to-Code Audit Rev2.0 closure, starting with Gate 1:
1. AUD-001 — AC-07 conflict / canonical acceptance criteria.
2. AUD-002 — trace all 42 FR rows to acceptance criteria, tests, source paths and evidence.
3. AUD-003 — map BR-001..BR-012 to invariants, aggregates, constraints and tests.

## Read before acting
- `AGENTS.md`
- `docs/engineering/control/CURRENT-STATE.md`
- `docs/engineering/control/ACTIVE-WORK.md`
- `docs/engineering/baseline/Rev2.0/README_AR.md`
- `docs/engineering/audit/Rev2.0/README_AR.md`
- `docs/engineering/audit/Rev2.0/registers/findings.csv`
- `docs/engineering/decisions/README.md`
- Repository `docs/AUTH-ADR-01-Session-Transport.md`
- Repository `docs/public-store-catalog-slice.md`

## Important distinctions
- Product Host is not the same as IdentitySpike or SessionTransportSpike.
- AUTH-ADR-01 overall status is Proposed in its header; some decisions within it are marked adopted for MVP.
- A passing PoC test suite does not prove Product Host authentication integration.
- Do not claim production readiness from CI success alone.
- Do not assume the old snapshot is current if HEAD changed.

## Suggested first prompt
"Run the start-session skill. Verify current HEAD and working tree. Then investigate AUD-001 only. Show the exact conflicting AC-07 definitions and their source locations, identify impacts on related FR/BR/UC/tests, recommend a canonical resolution, and wait for my approval before changing business meaning. Do not modify application code."
