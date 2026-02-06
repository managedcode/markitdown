# Conversations
any resulting updates to agents.md should go under the section "## Rules to follow"
When you see a convincing argument from me on how to solve or do something. add a summary for this in agents.md. so you learn what I want over time.
If I say any of the following point, you do this: add the context to agents.md, and associate this with a specific type of task.
if I say "never do x" in some way.
if I say "always do x" in some way.
if I say "the process is x" in some way.
If I tell you to remember something, you do the same, update


## Rules to follow
- Never introduce fallback logic that silently overrides user or config values; surface configuration errors instead of masking them in code.
- Keep `SegmentOptions.MaxParallelImageAnalysis` at `Math.Max(Environment.ProcessorCount * 4, 32)` and do not downscale it via runtime fallbacks.
- Treat non-positive `SegmentOptions.MaxParallelImageAnalysis` values as configuration errors—fail fast instead of defaulting to unlimited concurrency.
- Ensure document segments remain in source order with explicit numeric page/segment metadata—avoid relying on labels like "Page 1".
- When extracting images (or other artifacts), persist them to disk when a target path is supplied and record the file path in artifact metadata.
- Generate Markdown output from the ordered segment collection so it always reflects current segment content; avoid storing stale Markdown snapshots.
- Allow `ConvertAsync` (and related entry points) to accept caller-supplied options for AI/config overrides on a per-document basis.
- MIME handling: always use `ManagedCode.MimeTypes` for MIME constants, lookups, and validation logic.
- Treat this repository as a high-fidelity port of `microsoft-markitdown`: every test fixture copied from the upstream `tests/test_files/` directory must be referenced by .NET tests (either as positive conversions or explicit unsupported cases). No orphaned fixtures.
- CSV parsing must use the `Sep` library; avoid Sylvan or other CSV parsers for new or updated code.
- Format integration tasks: never break the project or existing tests, and validate new format handling against real sample files.
- Test fixtures must be surfaced via the auto-generated `TestAssetCatalog`; add binaries under `TestFiles/` and rely on its constants in tests.
- YouTube converter work: include at least one live integration test that exercises the real metadata provider (skip gracefully if the upstream API is unavailable) so the flow mirrors production behaviour.
- Never introduce test-only abstractions like `IAzureIntegrationSampleResolver` into the core library; keep cross-cutting helpers clean and production-ready.
- Image enrichment tasks: once OCR runs, send the artifact through the shared `IChatClient` prompt constants, capture a thorough visual description first, convert diagrams/schematics into Mermaid or structured tables, describe technical drawings in depth, and emit Markdown that follows `docs/MetaMD.md` and `docs/MetaMD-Examples.md`.
- Image AI enrichment must reject missing MIME metadata—surface the failure to callers instead of substituting fallback content types.
- Image enrichment tasks: once AI enrichment runs, strip any legacy/fallback image comments so only one `**Image:` placeholder and description remain in the final Markdown.
- Front matter titles must ignore metadata or image description comments—derive the title from the first real document text.
- When refactoring intelligence helpers, have them return explicit result data instead of relying on hidden side effects.
- Image placeholders must emit Markdown image links (`![alt](file.png)`) that reference persisted artifacts; only fall back to bold text when no file is available.
- If AI image enrichment yields no insight, log and continue instead of throwing—treat empty payloads as a soft failure.
- When executing tests, always include the `ManualConversionDebugTests` suite; treat its failures as blocking.
- Always run the full test suite after making changes and share the results with the user.
- Telemetry work: instrument both overall document processing time and per-page duration with real metrics alongside traces—include histogram/counter coverage so latency is observable at both levels.
- For large converters, structure them as partial classes and split related files into a dedicated subfolder.
- Markdown hygiene: strip non-breaking, zero-width, or other non-printable spaces; replace them with regular ASCII spaces so output never contains invisible characters like the long space before `Add`.
- Architecture revamps: adopt DI-first composition, expose per-request cloud model selection, and employ `System.IO.Pipelines` with optional parallel converter scheduling while keeping documentation and structure tidy.
- DOCX processing work: restructure element handling around pipeline-driven parallelism so enrichment and extraction avoid sequential bottlenecks while preserving output ordering.
- URL conversion APIs: expose Uri-based overloads so callers can supply strongly-typed endpoints without manual string normalization.
- Manual Azure config defaults: never auto-populate `AzureIntegrationConfigDefaults` from environment variables; keep the static placeholder JSON.
- Never use `MemoryStream` for conversion paths; rely on file-based processing instead of in-memory buffering.
- Disk-first refactors: put shared disk/workspace helpers into reusable base classes instead of hiding them as nested converter types.
- Document pipeline work: keep a single, well-defined flow that matches `docs/DocumentProcessingPipeline.md`, centralising common setup in the shared base converter and pushing OpenXML helpers into shared abstractions instead of per-converter copies; document tables/images behaviour in `docs/MetaMD.md`.
- Manual conversion diagnostics: persist manual harness output to disk and ensure MetaMD formatting includes image description blocks for every extracted artifact.
- Multi-page tables must emit `<!-- Table spans pages X-Y -->` comments, continuation markers for each affected page, and populate `table.pageStart`, `table.pageEnd`, and `table.pageRange` metadata so downstream systems can align tables with their source pages.
- PDF converters must honour `SegmentOptions.Pdf.TreatPagesAsImages`, rendering each page to PNG, running OCR/vision enrichment, and composing page segments with image placeholders plus recognized text whenever the option is enabled.
- Persist conversion workspaces through `ManagedCode.Storage` by allocating a unique, sanitized folder per document, copy the source file, store every extracted artifact via `IStorage`, and emit the final Markdown into the same folder.
- Root path configurability: `MarkItDownPathResolver` must support a configurable root via `MarkItDownOptions.RootPath` (non-DI) or `MarkItDownServiceBuilder.UseRootPath()` (DI); the resolver uses a lock-guarded double-check (not `Lazy<string>`) so `Configure()` and first access are atomic, and conflicting paths throw `InvalidOperationException` instead of being silently ignored.

# Repository Guidelines

## Project Structure & Module Organization
`MarkItDown.slnx` stitches together the core library under `src/MarkItDown` and the CLI scaffold in `src/MarkItDown.Cli`. The `MarkItDown` project hosts converters, options, and MIME helpers; keep new format handlers inside `Converters/` with focused folders. Integration and regression tests live in `tests/MarkItDown.Tests`, using `*Tests.cs` naming. The `microsoft-markitdown` directory mirrors the upstream Python project via submodule—update it only when syncing parity fixtures. Generated `bin/`, `obj/`, and `TestResults/` folders appear locally; avoid committing them.

## Build, Test, and Development Commands
- `dotnet restore MarkItDown.slnx` – hydrate solution-wide dependencies.
- `dotnet build MarkItDown.slnx` – compile all projects with analyzers enforced.
- `dotnet test MarkItDown.slnx` – run xUnit suites; fails on warnings because of solution settings.
- `dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"` – emit Cobertura XML under `tests/MarkItDown.Tests/TestResults/`.
- `dotnet run --project src/MarkItDown.Cli -- sample.pdf` – try the CLI (currently experimental) against a local asset.

## Coding Style & Naming Conventions
Projects target `net9.0`, `LangVersion` 13, `Nullable` enabled, and treat warnings as errors. Follow standard C# layout: four-space indents, braces on new lines, and `PascalCase` for types/methods, `camelCase` for locals and parameters. Prefer expression-bodied members only when they improve clarity. Use `var` when the right-hand side makes the type obvious. Keep XML documentation on public APIs and log messages actionable.

## Testing Guidelines
Tests use xUnit with Shouldly helpers; place fixtures alongside the code they cover. Name methods `MethodUnderTest_Scenario_Expectation` to match existing suites. When adding new converters, create integration tests under `tests/MarkItDown.Tests` that ensure round-trip Markdown and negative paths. Collect coverage with the command above and review the generated Cobertura report before submitting.

## Commit & Pull Request Guidelines
Recent history favors short, lower-case commit subjects (for example, `removecli`). Continue with concise, descriptive imperatives, optionally tagging scopes (`converter: add epub caption support`). Each PR should link related issues, outline behaviour changes, and note test or coverage results. Attach CLI output or screenshots when UX-facing changes occur, and call out any parity updates pulled from the Python submodule.

## Security & Configuration Notes
Respect `.gitmodules` and `Directory.Build.props`—they embed repository URLs, reproducible build settings, and authorship data. Never check in API keys or document samples that contain customer data. Configuration overrides belong in `MarkItDownOptions`; guard new options with sensible defaults to keep the library safe for unattended execution.
