# DropSort Media Library — Skills Plan

This file defines the AI-agent skills selected for **DropSort Media Library**.

Primary upstream source:

- Repository: `alirezarezvani/claude-skills`
- Upstream snapshot reviewed: `aa8d778811a557a2c28ccadda4cf3d0bd028a4cc`
- Source project contains both Core and POWERFUL engineering skills for Claude Code, Codex, Gemini and other agents.

The goal is **not** to activate every available skill on every task. Skills are selected by trigger so the project gets strong review/testing/safety coverage without needless context or over-engineering.

---

# 1. Always-On Core Skills

These are the default engineering skills for substantial DropSort work.

## `senior-architect`
Source:
`engineering-team/skills/senior-architect/SKILL.md`

Use for:
- modular-monolith boundaries;
- File Engine vs Media Library separation;
- application/domain/adapter boundaries;
- large refactors;
- dependency direction;
- architecture decisions.

## `code-reviewer`
Source:
`engineering-team/skills/code-reviewer/SKILL.md`

Use after meaningful implementation changes for:
- correctness;
- maintainability;
- complexity;
- coupling;
- regression risk;
- unsafe assumptions.

## `adversarial-reviewer`
Source:
`engineering-team/skills/adversarial-reviewer/SKILL.md`

Use after normal review, especially for:
- filesystem operations;
- Undo/Recovery;
- Relink;
- Qt races;
- stale authorization;
- TOCTOU;
- data-loss scenarios.

Potential user-file loss is always BLOCKER/CRITICAL.

## `tdd-guide`
Source:
`engineering-team/skills/tdd-guide/SKILL.md`

Use for:
- red/green/refactor workflows;
- failure-path tests;
- coverage analysis;
- deterministic tests;
- regression tests.

## `dependency-auditor`
Source:
`engineering/skills/dependency-auditor/SKILL.md`

Use when:
- adding/changing dependencies;
- preparing releases;
- reviewing packaging dependencies;
- checking supply-chain and unnecessary packages.

---

# 2. Database / Persistence Skills

## `database-designer`
Source:
`engineering/skills/database-designer/SKILL.md`

Use for:
- SQLite schema;
- indexes;
- constraints;
- migrations;
- transaction boundaries;
- catalog/journal integrity.

## `database-schema-designer`
Source:
`engineering/skills/database-schema-designer/SKILL.md`

Secondary schema-review skill for substantial future schema additions.

Do not invoke merely for small repository-query changes.

## `sql-database-assistant`
Source:
`engineering/skills/sql-database-assistant/SKILL.md`

Use for:
- query review;
- deterministic ordering;
- transaction behavior;
- performance/EXPLAIN work;
- SQLite integrity checks.

## `migration-architect`
Source:
`engineering/skills/migration-architect/SKILL.md`

Use only when a real schema/data/runtime migration is required.

DropSort rule: do not create migrations merely because a new phase exists.

---

# 3. Debugging / Repair / Performance Skills

## `focused-fix`
Source:
`engineering/skills/focused-fix/SKILL.md`

Use when a specific feature or regression fails and needs deep diagnosis without broad rewriting.

## `performance-profiler`
Source:
`engineering/skills/performance-profiler/SKILL.md`

Use for measured performance problems such as:
- large library scans;
- large result rendering;
- SQLite query regressions;
- poster cache behavior;
- startup performance.

Profile before optimizing.

## `codebase-onboarding`
Source:
`engineering/skills/codebase-onboarding/SKILL.md`

Use when a new agent/model takes over the repository and must map architecture and current implementation before editing.

This is particularly useful for Manus/Codex/Gemini independent-review tracks.

## `tech-debt-tracker`
Source:
`engineering/skills/tech-debt-tracker/SKILL.md`

Use to record genuine deferred debt and limitations without turning release tasks into unrelated refactors.

---

# 4. Security / Secrets / Release Safety

## `senior-security`
Source:
`engineering-team/skills/senior-security/SKILL.md`

Use for:
- threat modeling;
- secrets review;
- unsafe process execution;
- path/input trust boundaries;
- packaged-release security.

## `env-secrets-manager`
Source:
`engineering/skills/env-secrets-manager/SKILL.md`

Use for:
- TMDB credential handling;
- environment variables;
- secret scanning;
- release credential audit.

DropSort must never ship or log a TMDB token.

## `skill-security-auditor`
Source:
`engineering/skills/skill-security-auditor/SKILL.md`

Use before importing/installing any new external AI skill.

Check for:
- destructive commands;
- prompt injection;
- credential access;
- unexpected network calls;
- filesystem abuse;
- suspicious dependencies/scripts.

## `skill-tester`
Source:
`engineering/skills/skill-tester/SKILL.md`

Use for validating custom DropSort skills themselves.

## `ship-gate`
Source:
`engineering/skills/ship-gate/SKILL.md`

Mandatory for the final Windows V1 release gate.

Use for:
- pre-production pass/fail checks;
- temporary-artifact audit;
- hardcoded path audit;
- secret audit;
- release completeness.

---

# 5. Specification / Review Discipline

## `spec-driven-workflow`
Source:
`engineering/skills/spec-driven-workflow/SKILL.md`

Use for major phases/features where acceptance criteria must be locked before implementation.

## `self-eval`
Source:
`engineering/skills/self-eval/SKILL.md`

Use at major gates so the implementing agent must critically assess its own evidence and explicitly mark anything unverified.

## `pr-review-expert`
Source:
`engineering/skills/pr-review-expert/SKILL.md`

Use when changes are submitted through GitHub branches/PRs rather than direct local work.

## `technology-stack-evaluator`
Source:
`engineering-team/skills/technology-stack-evaluator/SKILL.md`

Use only when choosing a genuinely new technical tool, for example the final Windows packaging strategy.

Do not use it to reopen settled decisions such as Python/PySide6/SQLite.

---

# 6. Release / Delivery Skills

## `ci-cd-pipeline-builder`
Source:
`engineering/skills/ci-cd-pipeline-builder/SKILL.md`

Optional for GitHub Actions release/test automation.

Do not add cloud/DevOps infrastructure beyond what a local Windows desktop project actually needs.

## `changelog-generator`
Source:
`engineering/skills/changelog-generator/SKILL.md`

Use when preparing an actual tagged release/changelog.

## `runbook-generator`
Source:
`engineering/skills/runbook-generator/SKILL.md`

Optional for concise build/release/recovery procedures.

## `git-worktree-manager`
Source:
`engineering/skills/git-worktree-manager/SKILL.md`

Use when multiple implementation/review agents are intentionally working in parallel isolated branches/worktrees.

Do not create worktrees for simple single-agent edits.

---

# 7. DropSort Custom Skills

Generic skills do not know DropSort's non-negotiable safety rules. The project should retain dedicated custom skills under a local agent-skills directory such as `.codex/skills/` or the equivalent supported by the active agent.

## `dropsort-project-rules`
Purpose:
- Windows desktop;
- local-first;
- Python/PySide6/SQLite;
- modular monolith;
- no Docker/server/cloud;
- explicit architecture boundaries;
- strict scope control.

## `dropsort-file-safety`
Highest-priority custom skill.

Rules include:
- never overwrite destination;
- never automatically delete original media;
- every physical mutation goes through the File Engine;
- approved-root validation;
- no unsafe symlink/junction/reparse traversal;
- journal before mutation;
- verify filesystem result before catalog commit;
- reversible Move/Rename;
- preserve both files in ambiguous recovery;
- stale authorization/TOCTOU checks.

Lifecycle:

`REQUEST → PLAN → VALIDATE → JOURNAL → EXECUTE → VERIFY → DATABASE COMMIT → DONE`

## `dropsort-media-matching`
Rules for:
- movie/TV detection;
- filename parsing;
- title/year evidence;
- candidate scoring;
- ambiguity;
- confidence/review behavior.

Critical invariant:

`MATCHED != CATALOG IMPORT AUTHORIZATION != FILESYSTEM AUTHORIZATION`

## `dropsort-testing`
Mandatory failure/adversarial cases include:
- destination exists;
- source disappears;
- permission denied;
- case-insensitive collision;
- path identity changes;
- reparse/junction insertion;
- disk/database failure;
- interrupted cross-volume operation;
- source-removal failure;
- Undo after restart;
- ambiguous recovery;
- stale/double confirmation;
- missing external drive;
- wrong-file Relink;
- Qt late callback/shutdown races.

Never perform destructive tests on real user media.

## `dropsort-review`
Before GREEN ask:
- Can this lose a user file?
- Can it overwrite anything?
- Can it escape approved roots?
- Can authorization become stale?
- Is SQLite consistent with filesystem truth?
- Is Move/Rename reversible?
- Does recovery preserve ambiguity?
- Does UI bypass an application boundary?
- Are failure/race paths tested?
- Can secrets leak?

## `dropsort-qt-lifecycle`
Additional custom skill recommended for DropSort.

Check:
- retained QThread/task lifetime;
- stale callback rejection;
- destroyed-widget delivery;
- cooperative cancellation;
- close/shutdown while work is active;
- no filesystem/HTTP/database blocking work on the UI thread.

## `dropsort-windows-runtime`
Additional custom skill recommended for DropSort.

Check:
- Windows case folding;
- NTFS identity behavior;
- `st_dev` / `st_ino` portability;
- junction/reparse behavior;
- path aliases;
- spaces/Unicode;
- AppData/runtime paths;
- CWD independence;
- Explorer/default-player launching without `shell=True`.

## `dropsort-release-gate`
Additional custom skill recommended for the final V1 release.

Check that the release contains no:
- user DB;
- poster cache;
- logs;
- secrets;
- `.venv`;
- pytest/coverage data;
- `__pycache__`/`.pyc`;
- manual smoke scripts;
- temporary fixtures;
- machine-specific developer paths.

Also verify packaged resources, TMDB attribution, normal-user launch, `%LOCALAPPDATA%` runtime state, and clean-profile behavior.

---

# 8. Activation Matrix

## Any significant feature/refactor
Use:
- `senior-architect`
- `tdd-guide`
- `code-reviewer`
- `adversarial-reviewer`
- relevant DropSort custom skills

## Filesystem Move/Rename/Undo/Recovery
Also require:
- `dropsort-file-safety`
- `dropsort-testing`
- `dropsort-windows-runtime`
- `dropsort-review`

## SQLite/schema/query work
Also use as applicable:
- `database-designer`
- `sql-database-assistant`
- `migration-architect`

## TMDB/credentials/security
Also use:
- `senior-security`
- `env-secrets-manager`
- `dependency-auditor`

## Qt/background work
Also require:
- `dropsort-qt-lifecycle`

## New agent taking over repository
Use:
- `codebase-onboarding`
- `senior-architect`
- `self-eval`
- `code-reviewer`
- `adversarial-reviewer`

## Final V1 packaging/release
Require:
- `ship-gate`
- `dependency-auditor`
- `senior-security`
- `env-secrets-manager`
- `technology-stack-evaluator` when choosing packaging tooling
- `dropsort-release-gate`
- `dropsort-windows-runtime`
- `dropsort-file-safety`
- `dropsort-testing`
- `dropsort-review`

---

# 9. Skills Intentionally Not Default-Activated

The upstream repository contains many excellent skills that are irrelevant to the current DropSort architecture. Do not load them simply because they exist.

Examples not enabled by default:
- AWS/Azure/GCP architecture;
- Kubernetes;
- Terraform;
- microservices/backend-platform skills;
- React/Next.js frontend skills;
- ML/Data Science skills;
- marketing/sales/business skills;
- RAG/LLM infrastructure;
- browser automation;
- cloud observability stacks.

They may be considered only if a future explicit DropSort requirement genuinely needs them.

---

# 10. Rule for Adding More Skills

A new skill is accepted only when it has:

`Clear Responsibility + Clear Trigger + Clear Benefit`

Before importing an external skill:

`skill-security-auditor → skill-tester → project approval`

The objective is not the largest possible skill list. The objective is strong, relevant engineering coverage with minimal noise and maximum protection of user media.
