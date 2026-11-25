# RetailFlow Engineering Documentation

**Last Synchronized:** `2026-11-26` | **Code Snapshot:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d` | **Docs Baseline:** PR #3 (`e204c9e`)

This directory is the single, evidence-based engineering knowledge base for RetailFlow. Every document separates **Current State (As-Is)**, **Target State (To-Be)**, and **Gaps**.

---

## 1. Governance & AI Operating Memory
- [Repository Agent Contract](../AGENTS.md) — Always-on operating rules and boundaries.
- [Engineering Memory & AI Operating System](engineering/README.md) — Read order, canonical source hierarchy, and editing rules.
- Control State: [CURRENT-STATE.md](engineering/control/CURRENT-STATE.md) | [ACTIVE-WORK.md](engineering/control/ACTIVE-WORK.md) | [AI-HANDOFF.md](engineering/control/AI-HANDOFF.md) | [WORK-LOG.md](engineering/control/WORK-LOG.md)
- Active Registers: [decision-log.csv](engineering/registers/decision-log.csv) | [evidence-register.csv](engineering/registers/evidence-register.csv) | [open-work.csv](engineering/registers/open-work.csv)

## 2. Overview & Requirements Baseline (Gate 1 Canonical)
- [Project Overview](00-Project-Overview/Project-Overview.md) — Product purpose, multi-tenant model, actors, and glossary.
- [Requirements & Business Rules](01-Requirements/Requirements-and-Business-Rules.md) — Summary and entry point to canonical Gate 1 registers.
- [Use Case Specifications (UC-01–UC-10)](01-Requirements/Use-Case-Specifications.md) — Full use case flows, trust boundaries, Gherkin examples, and open decisions.
- [Acceptance Criteria Register](01-Requirements/Acceptance-Criteria.csv) & [AC Reconciliation](01-Requirements/Acceptance-Criteria-Reconciliation.md) — Canonical `AC-01`–`AC-08` and Rev2.0 reconciliation.
- [NFR Register](01-Requirements/NFR-Register.csv) — Canonical `NFR-001`–`NFR-009`.
- [Business Rules Traceability Matrix](01-Requirements/Business-Rules-Traceability-Matrix.csv) — `BR-001`–`BR-012` mapped to invariants and aggregates.

## 3. Architecture, Database, Security & API
- [System Architecture & Diagrams](02-Architecture/System-Architecture.md) — Clean Architecture / Modular Monolith layers, dependency graph, and Mermaid diagrams.
- [Database Design & Migrations](03-Database/Database-Design-and-Migrations.md) — `RetailFlowProduct` SQL Server schema, composite FKs, `Currencies` seed data, and migrations.
- [Security & Identity Architecture](04-Security-and-Identity/Security-and-Identity-Architecture.md) — Production tenant isolation vs. `IdentitySpike` & `SessionTransportSpike` PoCs.
- [Public Catalog API Reference](05-API/Public-Catalog-API.md) — Endpoints, query validation, `ProblemDetails`, and health probes.

## 4. Quality, Operations & Engineering Management
- [Testing & Verification](06-Quality-and-Testing/Testing-and-Verification.md) — Test suites across all solutions (`196` tests) and SQL Server testing rules.
- [Deployment & Operations](07-Deployment-and-Operations/Deployment-and-Configuration.md) — Azure App Service Linux target, `ConnectionStringResolver`, health checks, and CI.
- [Implementation Status & Roadmap](08-Engineering-Management/Implementation-Status-and-Roadmap.md) — What is implemented, in PoC, or deferred, plus technical debt.
- [Change Log & Open Decisions (Gate 1)](08-Engineering-Management/Change-Log-and-Open-Decisions.md) — `CR-G1-*` change requests and `OD-001`–`OD-008` open decisions.
- [Architecture Decision Records (ADRs)](08-Engineering-Management/Architecture-Decision-Records/ADR-Catalog.md) — Key architectural decisions (`ADR-001`–`ADR-004`).

## 5. Audit, Validation & Traceability
- [Comprehensive Audit Report](09-Audit-and-Traceability/Comprehensive-Audit-Report.md) — Full 20-section technical, security, and documentation audit.
- [Documentation Change Report](09-Audit-and-Traceability/Documentation-Change-Report.md) — Inventory of reviewed, updated, created, and retained files.
- [Gate 1 Validation Report](09-Audit-and-Traceability/Gate-1-Validation-Report.md) & [Gate 1 Closure Report](09-Audit-and-Traceability/Gate-1-Closure-Report.md) — Automated Gate 1 baseline checks and closure summary.
- Traceability Matrices & Registers:
  - [Documentation-Inventory.csv](09-Audit-and-Traceability/Documentation-Inventory.csv)
  - [Reference-Register.csv](09-Audit-and-Traceability/Reference-Register.csv)
  - [Requirements-Traceability-Matrix.csv](09-Audit-and-Traceability/Requirements-Traceability-Matrix.csv)
  - [Architecture-Traceability-Matrix.csv](09-Audit-and-Traceability/Architecture-Traceability-Matrix.csv)
  - [Security-Traceability-Matrix.csv](09-Audit-and-Traceability/Security-Traceability-Matrix.csv)
  - [Documentation-to-Code-Matrix.csv](09-Audit-and-Traceability/Documentation-to-Code-Matrix.csv)
  - [Findings.csv](09-Audit-and-Traceability/Findings.csv)

## 6. Slice Records & Draft ADRs
- [AUTH-ADR-01 — Session Transport](AUTH-ADR-01-Session-Transport.md) — Session transport ADR (`Proposed`, v0.14).
- [Public Store Catalog Slice](public-store-catalog-slice.md) — Vertical Slice 01 implementation record.
- [Product Host Bootstrap](product-host-bootstrap.md) — Initial host bootstrap record.
- [Phase 06 Identity Spike Status](phase-06-identity-spike-status.md) — Historical PoC implementation status.

## 7. Imported Reference Archives
- [Rev2.0 Engineering Baseline](engineering/baseline/RetailFlow_Engineering_Documentation_AtoZ_Rev2.0/README_AR.md)
- [Rev2.0 Documentation-to-Code Audit](engineering/audit/RetailFlow_Documentation_to_Code_Audit_Rev2.0/README_AR.md)
- [Original Phase 01–07 PDFs](engineering/source-material/README.md)
- [Package Metadata & Checksums](engineering/package-meta/PACKAGE-MANIFEST.json)
