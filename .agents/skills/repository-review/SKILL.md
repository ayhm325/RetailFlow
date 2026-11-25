---
name: repository-review
description: Perform a bounded but evidence-based review of RetailFlow code, architecture, tests, security, and CI for a specified scope or finding.
---
# Repository review

1. Confirm branch/commit and scope.
2. Inventory all relevant files, not only obvious implementation files.
3. Follow request paths from endpoint through Application, Domain, Infrastructure, database, and tests.
4. Inspect negative paths, concurrency, authorization, tenant boundaries, error handling, and operational configuration.
5. Use exact line/path references in findings.
6. Do not infer behavior from class names or documentation alone.
7. Run available tests when requested/possible; otherwise clearly state not run.
8. Rank findings by demonstrated impact and likelihood; distinguish confirmed defects from risks/questions.
9. Produce remediation steps and verification criteria, not speculative rewrites.
