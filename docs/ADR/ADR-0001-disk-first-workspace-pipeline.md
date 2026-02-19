# ADR-0001: Disk-First Workspace Pipeline

Status: Implemented  
Date: 2026-02-19  
Related Features: `docs/Features/disk-first-conversion-pipeline.md`, `docs/Features/structured-docx-pdf-conversion.md`  
Supersedes: none  
Superseded by: none

---

## Implementation plan (step-by-step)

- [x] Analyze existing conversion source materialization and workspace persistence
- [x] Record decision and trade-offs for disk-first contract
- [x] Map decision invariants to tests/docs
- [x] Link architecture and feature documentation

---

## Context

- The project converts potentially large documents (PDF/Office/archives/media) and must avoid memory pressure and hidden buffering.
- Existing project rules explicitly prohibit `MemoryStream`-based conversion paths.
- Conversion also needs stable artifact persistence and deterministic cleanup.
- Goal: enforce a file-backed conversion path so all converters/middleware operate on consistent workspace resources.
- Non-goal: replacing every in-memory helper in unrelated utility paths outside conversion flow.

---

## Stakeholders (who needs this to be clear)

| Role | What they need to know | Questions this ADR must answer |
| --- | --- | --- |
| Product / Owner | Stable conversion for large files | Can large files be processed reliably? |
| Engineering | Required pipeline invariants | Where must disk materialization happen? |
| DevOps / SRE | Temp storage/cleanup implications | How do we avoid leaked workspaces? |
| QA | Testable behavior guarantees | Which tests prove disk-first behavior? |

---

## Decision

The conversion pipeline uses disk-backed workspace materialization as the primary contract for source and artifact handling.

Key points:

- Converters materialize input via shared base abstractions before extraction.
- Artifacts are persisted through workspace/storage abstractions and surfaced in metadata.
- Conversion result composition happens after extraction/middleware on persisted resources.

---

## Diagram

```mermaid
flowchart LR
  A["Input stream/path/url"] --> B["Materialize source to workspace"]
  B --> C["Converter extraction"]
  C --> D["Persist artifacts"]
  D --> E["Middleware enrichment"]
  E --> F["Markdown composition"]
  F --> G["Result + workspace metadata"]
```

---

## Alternatives considered

### Option A: In-memory buffering during conversion

- Pros: fewer temp files, simpler initial implementation
- Cons: poor behavior on large payloads, higher GC/LOH pressure, unstable memory profile
- Rejected because: violates repository rule and does not scale safely.

### Option B: Hybrid memory-first with fallback spill-to-disk

- Pros: can be fast for tiny files
- Cons: hidden mode switches, inconsistent behavior, harder diagnostics
- Rejected because: increases complexity and invites silent fallback behavior.

---

## Consequences

### Positive

- Predictable resource profile for large conversions.
- Consistent artifact paths for downstream enrichment/debugging.
- Easier enforcement of workspace persistence rules.

### Negative / risks

- More disk I/O and temporary file management complexity.
- Risk of stale workspace data if cleanup paths break.
- Mitigation: centralize workspace lifecycle and test factory/workspace behavior.

---

## Impact

### Code

- Affected modules / services: `Core`, `Conversion`, converter base classes.
- New boundaries / responsibilities: converter extraction assumes persisted sources.
- Feature flags / toggles: pipeline/workspace options in `MarkItDownOptions` and `ConversionRequest`.

### Data / configuration

- Data model / schema changes: none.
- Config changes: workspace/storage options determine artifact persistence location.
- Backwards compatibility strategy: keep public conversion APIs unchanged.

### Documentation

- Feature docs to update: disk-first and structured conversion feature specs.
- Testing docs to update: conversion and workspace tests mapping.
- Architecture docs to update: module/contracts map in architecture overview.
- `docs/Architecture/Overview.md` updates: include workspace module and links.
- Notes for `AGENTS.md`: keep explicit prohibition of memory-stream conversion paths.

---

## Verification

### Objectives

- Prove conversion entry points work with file-backed materialization.
- Prove workspace factory persists artifacts and handles path policies.
- Prove failures still return actionable conversion errors.

### Test environment

- Environment: local .NET SDK and in-repo test assets.
- Data/reset strategy: deterministic fixture files and generated catalog.
- External dependencies: not required for core disk-first behavior tests.

### Test commands

- build: `dotnet build MarkItDown.slnx`
- test: `dotnet test MarkItDown.slnx`
- format: `dotnet format MarkItDown.slnx`
- coverage: `dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"`

### New or changed tests

| ID | Scenario | Level (Unit / Int / API / UI) | Expected result | Notes / Data |
| --- | --- | --- | --- | --- |
| TST-001 | Workspace factory creates and resolves artifact directories | Integration | Files/artifacts persisted with expected policy | `tests/MarkItDown.Tests/Conversion/ArtifactWorkspaceFactoryTests.cs` |
| TST-002 | Non-seekable stream conversion still succeeds | Integration | Pipeline handles buffered disk path | `tests/MarkItDown.Tests/MarkItDownTests.cs` |

### Regression and analysis

- Regression suites: `tests/MarkItDown.Tests/MarkItDownIntegrationTests.cs`, converter suites.
- Static analysis: analyzer-enforced build in CI/local.
- Monitoring during rollout: conversion failure counters and duration telemetry.

---

## Rollout and migration

- Migration steps: keep converters aligned with base materialization pattern.
- Backwards compatibility: no public API break required.
- Rollback: revert converter/base changes that violate prior behavior.

---

## References

- `docs/DocumentProcessingPipeline.md`
- `src/MarkItDown/Converters/Base/DocumentPipelineConverterBase.cs`
- `src/MarkItDown/Conversion/ArtifactWorkspaceFactory.cs`
- `tests/MarkItDown.Tests/Conversion/ArtifactWorkspaceFactoryTests.cs`
