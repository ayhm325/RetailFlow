# RetailFlow Engineering Documentation

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

This directory is the single, evidence-based engineering knowledge base for RetailFlow. Every document separates **Current State (As-Is)**, **Target State (To-Be)**, and **Gaps**.

## Documentation Index

### Overview & Requirements
- [Project Overview](00-Project-Overview/Project-Overview.md) — Product purpose, multi-tenant model, actors, and glossary.
- [Requirements & Business Rules](01-Requirements/Requirements-and-Business-Rules.md) — Functional, non-functional, and business rules mapped to actual implementation.

### Architecture, Database & API
- [System Architecture & Diagrams](02-Architecture/System-Architecture.md) — Clean Architecture / Modular Monolith layers, dependency graph, and Mermaid diagrams.
- [Database Design & Migrations](03-Database/Database-Design-and-Migrations.md) — `RetailFlowProduct` SQL Server schema, composite FKs, `Currencies` seed data, and migrations.
- [Public Catalog API Reference](05-API/Public-Catalog-API.md) — Endpoints, query validation, `ProblemDetails`, and health probes.

### Security, Identity & PoCs
- [Security & Identity Architecture](04-Security-and-Identity/Security-and-Identity-Architecture.md) — Production tenant isolation vs. `IdentitySpike` & `SessionTransportSpike` PoCs.
- [AUTH-ADR-01 — Session Transport](AUTH-ADR-01-Session-Transport.md) — Session transport ADR (`Proposed`, v0.14).
- [Phase 06 Identity Spike Status](phase-06-identity-spike-status.md) — Historical PoC implementation status.

### Quality, Deployment & Management
- [Testing & Verification](06-Quality-and-Testing/Testing-and-Verification.md) — Test suites across all 4 solutions (`196` tests) and SQL Server testing rules.
- [Deployment & Operations](07-Deployment-and-Operations/Deployment-and-Configuration.md) — Azure App Service Linux target, `ConnectionStringResolver`, health checks, and CI.
- [Implementation Status & Roadmap](08-Engineering-Management/Implementation-Status-and-Roadmap.md) — What is implemented, in PoC, or deferred, plus technical debt.
- [Architecture Decision Records (ADRs)](08-Engineering-Management/Architecture-Decision-Records/ADR-Catalog.md) — Key architectural decisions (`ADR-001`–`ADR-004`).

### Legacy / Slice-Specific Records (Preserved)
- [Product Host Bootstrap](product-host-bootstrap.md) — Initial host bootstrap documentation (updated with current status notes).
- [Public Store Catalog Slice](public-store-catalog-slice.md) — Vertical Slice 01 summary.

### Audit & Traceability
- [Comprehensive Audit Report](09-Audit-and-Traceability/Comprehensive-Audit-Report.md) — Full 20-section technical, security, and documentation audit.
- [Documentation Change Report](09-Audit-and-Traceability/Documentation-Change-Report.md) — Inventory of reviewed, updated, created, and retained files.
- Traceability Matrices & Registers:
  - [Documentation-Inventory.csv](09-Audit-and-Traceability/Documentation-Inventory.csv)
  - [Reference-Register.csv](09-Audit-and-Traceability/Reference-Register.csv)
  - [Requirements-Traceability-Matrix.csv](09-Audit-and-Traceability/Requirements-Traceability-Matrix.csv)
  - [Architecture-Traceability-Matrix.csv](09-Audit-and-Traceability/Architecture-Traceability-Matrix.csv)
  - [Security-Traceability-Matrix.csv](09-Audit-and-Traceability/Security-Traceability-Matrix.csv)
  - [Documentation-to-Code-Matrix.csv](09-Audit-and-Traceability/Documentation-to-Code-Matrix.csv)
  - [Findings.csv](09-Audit-and-Traceability/Findings.csv)
