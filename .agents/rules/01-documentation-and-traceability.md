---
trigger: model_decision
description: Apply when creating, reviewing, changing, or closing project documentation, requirements, business rules, acceptance criteria, audit findings, or traceability registers.
---
# Documentation and traceability
- Markdown, CSV, YAML, and Mermaid sources are editable masters; PDF/DOCX are generated publication artifacts.
- Maintain stable IDs. Never renumber or reuse IDs without an explicit migration map.
- Link each requirement to applicable use cases, business rules, NFRs, acceptance criteria, test IDs, source paths, CI evidence, commit SHA, status, and deviations where applicable.
- Do not use generic placeholders such as "test required" as acceptance evidence.
- Keep Current and Target artifacts distinct. Label diagrams and schemas with status and source commit.
- When evidence is missing, mark it Missing/Unknown; do not infer it.
- Update `doc/09-traceability/Feature-Code-Test-Matrix.md` and `doc/10-open-items/Production-Gaps-and-Decision-Backlog.md` when verified facts or gap statuses change.
