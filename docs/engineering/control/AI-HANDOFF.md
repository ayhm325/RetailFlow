# AI Handoff — Start Here Next Session

> Updated after Gate 1 closure and PR #3 merge (`e204c9e`). Always verify current `HEAD` before acting.

## Repository anchor
- Repository: `ayhm325/RetailFlow`
- Default branch (`main`): `e204c9e6f0d570be24fbdb2750889dd464d1e077` (PR #3 merged)
- Active working branch: `feature/public-store-catalog-read` (`2c020de`)
- Verified Product Host code/test snapshot: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`
- First action: inspect `git status --short --branch` and `git rev-parse HEAD` to confirm whether this SHA is still current.

## Active work
Gate 1 (`AUD-001`, `AUD-002`, `AUD-003`, `AUD-007`) is complete and validated (`scripts/validate-gate1-baseline.ps1`: `773/773` pass). Next up:
1. Product Owner sign-off on `CR-G1-001` and `OD-001`–`OD-005` (`docs/08-Engineering-Management/Change-Log-and-Open-Decisions.md`).
2. **Gate 2 (`AUD-004`, `AUD-005`)**: Separate `OrderStatus`/`FulfillmentStatus`/`PaymentStatus` models and design atomic Checkout idempotency contract.

## Read before acting
- `AGENTS.md`
- `docs/engineering/control/CURRENT-STATE.md`
- `docs/engineering/control/ACTIVE-WORK.md`
- `docs/01-Requirements/Requirements-and-Business-Rules.md`
- `docs/08-Engineering-Management/Change-Log-and-Open-Decisions.md`
- `docs/engineering/audit/RetailFlow_Documentation_to_Code_Audit_Rev2.0/registers/findings.csv`
- `docs/AUTH-ADR-01-Session-Transport.md`
- `docs/public-store-catalog-slice.md`

## Important distinctions
- Product Host (`src/`) is **not** integrated with `experiments/IdentitySpike` or `experiments/SessionTransportSpike`.
- `AUTH-ADR-01` overall status remains `Proposed` (`v0.14`); some MVP sub-decisions are marked adopted inside the draft.
- A passing PoC test suite does not prove Product Host authentication integration.
- Do not claim production readiness from CI success alone.

## Suggested first prompt
"Run the start-session skill. Verify current HEAD and working tree on feature/public-store-catalog-read. Review Gate 2 findings AUD-004 and AUD-005 against Phase 04/05 and open decisions OD-001..OD-008, propose the separated Order/Fulfillment/Payment state models and atomic Checkout idempotency contract, and wait for approval before modifying application code."
