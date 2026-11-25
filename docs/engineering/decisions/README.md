# Architecture Decision Records

## Policy
- An ADR captures one material decision and its context.
- Status lifecycle: `Proposed` -> owner review -> `Accepted` or `Rejected`; an accepted ADR may later become `Superseded`.
- AI agents may draft and research ADRs but cannot accept them.
- Accepted ADRs are not edited to erase history. Create a superseding ADR and link both.
- An Accepted ADR does not prove the decision has been implemented. Track implementation separately in traceability/evidence.
- Keep sub-decisions explicit. An overall Proposed ADR can contain scoped adopted choices; never promote the overall status based on one adopted section.

## Current decision inventory
- `AUTH-ADR-01-Session-Transport.md` exists in repository `docs/`; its overall header status is Proposed in the inspected snapshot. Some contained MVP sub-decisions are marked Adopted for MVP. Reconcile the ADR status model before treating it as an accepted overall decision.
- Other baseline decisions are imported in `baseline/Rev2.0/registers/decision-register.csv` and `open-decisions.csv`. Validate each against source ADRs and owner approval before implementation.

## New ADR naming
`ADR-NNNN-short-kebab-case.md`

Use `ADR_TEMPLATE.md`.
