---
name: release-readiness
description: Evaluate whether a RetailFlow change or release has enough technical, security, operational, data recovery, and evidence gates to proceed.
---
# Release readiness assessment

Review:
- approved scope and requirements/AC;
- architecture and security decisions;
- tenant isolation and authorization tests;
- migration compatibility and data integrity;
- CI/build/unit/integration/browser results;
- configuration and secret handling;
- deployment topology, TLS, proxy headers, health checks;
- logs/metrics/traces and alerting;
- backup, restore, RPO/RTO and recovery rehearsal;
- performance/load and capacity targets approved by owner;
- rollback/forward-fix and incident runbooks;
- unresolved risks and explicit owner acceptance.

Do not invent thresholds. Mark each gate Pass / Fail / Not evidenced / Not applicable with evidence. Build success alone is not production readiness.
