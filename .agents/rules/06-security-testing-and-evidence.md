---
trigger: model_decision
description: Apply to security-sensitive code, authentication, authorization, tenant isolation, tests, CI, release readiness, observability, or audit evidence.
---
# Security, testing, and evidence
- Threat-model trust boundaries and attacker-controlled inputs.
- Apply least privilege, secure defaults, secret hygiene, safe logging, and explicit authorization checks.
- Do not treat CORS, route obscurity, or client-side filtering as authorization.
- Prefer deterministic tests; use real SQL Server integration tests for SQL-specific behavior and browser tests for browser/session semantics.
- Report test command, environment, result, skipped tests, and limitations. A test not run is not a pass.
- Do not claim production readiness from build/CI success alone; require operational, security, backup/restore, load, monitoring, and deployment evidence appropriate to scope.
- If a credential is found committed, do not reproduce it; assess exposure, recommend rotation if reused, and move configuration to secure CI secrets or short-lived generated credentials.
