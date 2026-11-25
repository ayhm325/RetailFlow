# Official Agent/IDE Documentation — Research Register

Reviewed: 2026-11-26. Product capabilities and supported file discovery can change; re-check the linked official page when upgrading the client.

## Google Antigravity
1. Rules and activation modes: https://antigravity.google/docs/rules/
   - Root `AGENTS.md` / `GEMINI.md` are plain Markdown and always active.
   - `.agents/rules/*.md` requires YAML frontmatter with a valid `trigger`: `always_on`, `model_decision`, `glob`, or `manual`.
   - Rules are cumulative; immediate children of `.agents/rules/` are scanned. Do not nest rules without a manifest.
2. Prompting and instruction layers: https://antigravity.google/docs/learn/prompting
   - Put persistent project constraints in workspace rules; personal cross-project preferences belong in global rules; procedural workflows belong in Skills.
3. Customization discovery: https://antigravity.google/docs/customization-discovery/
   - Workspace customizations are version-controllable in `.agents/`; Skills live in `.agents/skills/<skill-name>/SKILL.md`.
   - Scan depth is shallow; a Skill directory should be directly under `skills/`.
4. Agent Skills: https://antigravity.google/docs/ide/skills
   - Skills are reusable, on-demand bundles with instructions and optional scripts/references.
5. Workflows-to-Skills migration: https://antigravity.google/docs/migration/workflows-to-skills
   - Antigravity adopted the open Agent Skills format in May 2026; legacy Workflows are deprecated. This package uses Skills, not legacy workflows.

## GitHub Copilot
6. Repository custom instructions: https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/add-custom-instructions/add-repository-instructions
   - `.github/copilot-instructions.md` is repository-wide guidance; `AGENTS.md` is recognized for agents; path-specific instructions use `.github/instructions/*.instructions.md`.
7. Prompt files: https://docs.github.com/en/copilot/concepts/prompting-and-context/prompt-files
   - Reusable interaction prompts use `.prompt.md` in `.github/prompts/`.
8. Customization cheat sheet: https://docs.github.com/en/copilot/reference/customization-cheat-sheet
   - Distinguishes always-on custom instructions from task-specific prompt files and custom agents.

## OpenAI Codex
9. Iterating Development Workflows with Codex: https://developers.openai.com/cookbook/examples/codex/iterating-development-workflows-with-codex
   - Describes `AGENTS.md` as an optional recognized persistent repository guidance file and suggests durable plan/context/build-log conventions.
10. Codex AGENTS.md guidance: https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.3-codex
   - Describes discovery and merging of AGENTS.md from global/repository/current-directory scope. Verify exact behavior for the installed Codex version.

## Claude Code
11. Claude project context: https://support.claude.com/en/articles/14553240-give-claude-context-claude-md-and-better-prompts
   - `CLAUDE.md` provides persistent project context. This package keeps it as a short pointer to canonical `AGENTS.md` to avoid divergent copies.

## Engineering policy references
12. Architecture Decision Records: https://adr.github.io/
13. Agent Skills open specification: https://agentskills.io/specification

## Interpretation policy
- Official vendor docs establish how a particular client discovers/loads instructions; they do not prove the model will follow them perfectly.
- Repository rules guide behavior but do not provide deterministic enforcement. Use CI, scripts, tests, branch protection, and human review for enforceable gates.
- Keep a single canonical policy and short client adapters. Avoid four divergent instruction systems.
- The package does not install hooks or execute shell commands automatically. The included validation script is an explicit local check.
