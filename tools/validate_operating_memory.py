#!/usr/bin/env python3
"""Validate the static structure of RetailFlow's AI operating memory.
This script does not validate project code, run tests, or prove an agent loaded instructions.
"""
from pathlib import Path
import csv, sys

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    "AGENTS.md",
    "docs/engineering/README.md",
    "docs/engineering/control/CURRENT-STATE.md",
    "docs/engineering/control/ACTIVE-WORK.md",
    "docs/engineering/control/AI-HANDOFF.md",
    "docs/engineering/control/WORK-LOG.md",
    ".agents/rules/00-operating-contract.md",
    ".agents/skills/start-session/SKILL.md",
    ".agents/skills/close-task/SKILL.md",
    "docs/engineering/decisions/ADR_TEMPLATE.md",
    "docs/engineering/references/official-sources.md",
]
errors = []
for item in REQUIRED:
    if not (ROOT / item).is_file():
        errors.append(f"Missing required file: {item}")

# Antigravity rule files must be direct children and have supported trigger frontmatter.
rules_dir = ROOT / ".agents/rules"
for p in sorted(rules_dir.glob("*.md")):
    content = p.read_text(encoding="utf-8")
    if not content.startswith("---\n"):
        errors.append(f"Rule missing YAML frontmatter: {p.relative_to(ROOT)}")
        continue
    front = content.split("---\n", 2)[1]
    if not any(f"trigger: {v}" in front for v in ("always_on", "model_decision", "glob", "manual")):
        errors.append(f"Rule has unsupported/missing trigger: {p.relative_to(ROOT)}")
    if "trigger: glob" in front and "globs:" not in front and "glob:" not in front:
        errors.append(f"Glob rule missing globs field: {p.relative_to(ROOT)}")

# Skills must be immediate folders under skills/ with SKILL.md.
skills_dir = ROOT / ".agents/skills"
for d in sorted(p for p in skills_dir.iterdir() if p.is_dir()):
    skill = d / "SKILL.md"
    if not skill.is_file():
        errors.append(f"Skill missing SKILL.md: {d.relative_to(ROOT)}")
    else:
        c = skill.read_text(encoding="utf-8")
        if not c.startswith("---\n") or "name:" not in c.split("---\n", 2)[1] or "description:" not in c.split("---\n", 2)[1]:
            errors.append(f"Skill missing required metadata: {skill.relative_to(ROOT)}")

# Validate imported audit CSV structure and unique finding IDs.
finding_csv = ROOT / "docs/engineering/audit/RetailFlow_Documentation_to_Code_Audit_Rev2.0/registers/findings.csv"
if finding_csv.exists():
    with finding_csv.open(encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    ids = [r.get("Finding ID", "") for r in rows]
    if len(ids) != len(set(ids)):
        errors.append("Duplicate Finding ID values in imported findings.csv")
    if not rows:
        errors.append("Imported findings.csv is empty")
else:
    errors.append("Imported audit findings.csv not found")

if errors:
    print("VALIDATION FAILED")
    for e in errors:
        print(f"- {e}")
    sys.exit(1)

print("VALIDATION PASSED")
print(f"Root: {ROOT}")
print(f"Rules: {len(list(rules_dir.glob('*.md')))}")
print(f"Skills: {len([p for p in skills_dir.iterdir() if p.is_dir()])}")
print("Note: this validates package structure only; it does not verify client discovery or project code.")
