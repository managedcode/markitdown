---
name: mcaf-architecture-overview
description: "Create or update `docs/Architecture/Overview.md` for a repository: map modules and boundaries, add a Mermaid module diagram, document dependency rules, and link to ADRs/features. Use when onboarding, refactoring, adding modules, or when the repo lacks a clear global architecture map."
compatibility: "Requires repository write access; produces Markdown docs with Mermaid diagrams."
---

# MCAF: Architecture Overview

## Output

- `docs/Architecture/Overview.md` (create or update)

## Architecture Thinking (keep it a map)

This doc is the **global map**: boundaries, modules, and dependency rules.

- Keep it lean and structural:
  - modules/boundaries + responsibility + dependency direction
  - at least one Mermaid module/boundary diagram
- Keep behaviour out of the overview:
  - feature flows live in `docs/Features/*`
  - decision-specific diagrams/invariants live in `docs/ADR/*`
- Anti-“AI slop” rule: never invent components/services/DBs — only document what exists (or what this change will explicitly add).

## Workflow

1. Open `docs/Architecture/Overview.md` if it exists; otherwise start from `docs/templates/Architecture-Template.md`.
   - Ensure it contains a short `## Scoping (read first)` section (this is how we prevent “scan everything” behaviour).
2. Identify the **real** top-level boundaries:
   - entry points (HTTP/API, CLI, UI, jobs, events)
   - modules/layers (group by folders/namespaces, not individual files)
   - external dependencies (only those that actually exist)
3. Fill the **Summary** so a new engineer can orient in ~1 minute.
4. Draw the Mermaid diagram as a **module map**:
   - keep it small (roughly 8–15 nodes)
   - label arrows with meaning (calls, events, reads/writes)
   - don’t invent DB/queues/services that aren’t present
5. Fill the modules table:
   - one row per module/service
   - responsibilities and “depends on” must be concrete
6. Write explicit dependency rules:
   - what is allowed
   - what is forbidden
   - how integration happens (sync / async / shared lib)
7. Add a short “Key decisions (ADRs)” section:
   - link to the ADRs that define boundaries, dependencies, and major cross-cutting patterns
   - keep it link-based (no detailed flows here)
8. Link out to deeper docs:
   - ADRs for key decisions
   - Features for behaviour details
   - Testing/Development for how to run and verify

## Guardrails

- Do not list every file/class. This is a **map**, not an inventory.
- Keep the document stable: update it when boundaries or interactions change.
