---
trigger: model_decision
description: Apply to architecture, module boundaries, dependencies, authentication, authorization, tenancy, integration patterns, deployment topology, or material technology choices.
---
# Architecture and ADR rules
- Inspect the current solution and dependency graph before proposing a design.
- Preserve Domain/Application/Infrastructure/API boundaries and existing composition patterns unless an Accepted ADR explicitly changes them.
- Separate product decisions, architecture decisions, implementation facts, and experiments.
- Material decisions require an ADR with Context, Decision Drivers, Options, Decision, Consequences, Security, Operations, Migration/Rollback, and Verification.
- The agent can recommend but cannot approve. Keep status Proposed until the owner accepts it.
- A new ADR supersedes an old one by reference; retain both.
- Cite current official documentation and authoritative engineering references, including version/date and applicability. Explain conflicts rather than cherry-picking.
