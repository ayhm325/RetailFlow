# AI Handoff — Start Here Next Session

> Updated after Corrective Engineering Pass (`RF-REV-001`–`RF-REV-010`) on `feature/public-store-catalog-read` (`37f80f5` + local uncommitted fixes). Always verify current `HEAD` before acting.

## Repository anchor
- Repository: `ayhm325/RetailFlow`
- Active working branch: `feature/public-store-catalog-read` (`37f80f537b62cf21a6d50c1885124013406e9914` + local corrective pass)
- First action: inspect `git status --short --branch` and `git rev-parse HEAD` to confirm whether this SHA is still current.

## Active work
- Backend implementation (`Public Catalog`, `Auth/ITicketStore/CSRF`, `Merchant`, `Platform`, `Cart`, `Checkout`, `Orders`) and `RF-REV-001`–`RF-REV-010` corrective fixes are verified locally (`11/11` Domain unit tests + `65/65` SQL integration tests).
- Next:
  1. Review local corrective changes (`git diff`) and push to `origin/feature/public-store-catalog-read` to verify remote CI (`.github/workflows/ci.yml` `RF-REV-001`).
  2. Obtain Owner sign-off on remaining open decisions (`OD-003`, `OD-004`, `OD-005`, `OD-007`, refund policy after COD collection, `AUD-010` numeric SLOs).
  3. Proceed to React + TypeScript SPA frontend (`wwwroot`) or Gate 3/4 diagram & stale-lease recovery worker tasks.

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
