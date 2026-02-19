# AGENTS.md

Project: ManagedCode.MarkItDown
Stack: .NET 10, C# 14, xUnit, Spectre.Console, Azure/OpenAI integrations, GitHub Actions

Follows [MCAF](https://mcaf.managed-code.com/)

---

## Conversations (Self-Learning)

Learn the user's habits, preferences, and working style. Extract rules from conversations, save to "## Rules to follow", and generate code according to the user's personal rules.

Update mechanism:

- Before any task, evaluate the latest user message.
- If a new permanent rule/correction/preference/process appears, update `AGENTS.md` first.
- Put new persistent rules under `## Rules to follow`.
- If no new permanent rule appears, do not update this file.

Extraction guidelines:

- Add rules when user states "never", "always", "remember", "the process is", "from now on".
- Treat strong frustration/repetition as high-priority permanent rules.
- Ignore one-off/temporary instructions (for this task only).

---

## Rules to follow (Mandatory, no exceptions)

### Commands

- build: `dotnet build MarkItDown.slnx`
- test: `dotnet test MarkItDown.slnx`
- format: `dotnet format MarkItDown.slnx`
- analyze: `dotnet build MarkItDown.slnx -p:RunAnalyzers=true`
- coverage: `dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"`

### Task Delivery (ALL TASKS)

- Always start from the architecture map in `docs/Architecture/Overview.md`.
- Define in-scope/out-of-scope before implementation.
- Use only the minimum required context; do not scan the whole repo without need.
- If a task matches an existing skill, follow that skill workflow.
- Analyze current behavior first, then implement.
- Implement code and tests together.
- If `build` is separate from `test`, run `build` before `test`.
- Run the full test suite after making changes and share results.
- When executing tests, always include `ManualConversionDebugTests`; treat failures as blocking.
- Always run required commands yourself; do not ask the user to run them.
- When asked to adopt an external tutorial/framework, execute the tutorial checklist end-to-end and document the result in repo docs.

### Documentation (ALL TASKS)

- All docs live in `docs/`.
- Global architecture entry point: `docs/Architecture/Overview.md`.
- Keep architecture docs navigational (diagrams + links), and push detailed behavior to `docs/Features/*` and decisions to `docs/ADR/*`.
- Single source of truth: link instead of duplicating large blocks across docs.
- Remove template placeholders (`TEMPLATE ONLY`, `TODO`, `...`) in real docs.
- Update feature docs when behavior changes.
- Update ADRs when architecture/contract decisions change.
- Keep Mermaid diagrams valid and renderable.
- Installer sync rule: if install-relevant assets change (`scripts/mcaf-install.sh`, `docs/templates/`, `skills/`), keep remote installer/docs consistent.

### Testing (ALL TASKS)

- Prefer integration/API/UI tests over isolated unit tests for behavior verification.
- Use real dependencies for internal systems; mocks are only for external third-party systems.
- Every behavior change must have meaningful automated test coverage.
- Do not delete/weaken tests to make CI green.
- Flaky tests are failures; fix root cause.
- Use coverage to find blind spots, not as a vanity metric.
- Integration/live tests must fail loudly on real provider/auth/network errors; never swallow with `catch { return; }`.

### Repository-specific mandatory rules

- Never introduce fallback logic that silently overrides user or config values; surface configuration errors instead of masking them in code.
- Keep `SegmentOptions.MaxParallelImageAnalysis` at `Math.Max(Environment.ProcessorCount * 4, 32)` and do not downscale it via runtime fallbacks.
- Treat non-positive `SegmentOptions.MaxParallelImageAnalysis` values as configuration errors—fail fast instead of defaulting to unlimited concurrency.
- Ensure document segments remain in source order with explicit numeric page/segment metadata—avoid relying on labels like "Page 1".
- When extracting images (or other artifacts), persist them to disk when a target path is supplied and record the file path in artifact metadata.
- Generate Markdown output from the ordered segment collection so it always reflects current segment content; avoid storing stale Markdown snapshots.
- Allow `ConvertAsync` (and related entry points) to accept caller-supplied options for AI/config overrides on a per-document basis.
- MIME handling: always use `ManagedCode.MimeTypes` for MIME constants, lookups, and validation logic.
- Treat this repository as a high-fidelity port of `microsoft-markitdown`: every fixture copied from upstream `tests/test_files/` must be referenced by .NET tests (positive conversion or explicit unsupported case).
- CSV parsing must use the `Sep` library; avoid Sylvan or other CSV parsers for new/updated code.
- Format integration tasks: never break the project or existing tests, and validate new format handling against real sample files.
- Test fixtures must be surfaced via generated `TestAssetCatalog`; add binaries under `TestFiles/` and use catalog constants in tests.
- YouTube converter work must include at least one live integration test using the real metadata provider (skip gracefully if upstream API unavailable).
- Media routing: if `StreamInfo` resolves to `audio/*` or `video/*` (uploaded media), do not route through `YouTubeUrlConverter`.
- For `video/*` inputs, do not use local audio-transcriber fallback; enforce Azure Video Indexer media-provider flow (upload, wait for `Processed`, then read transcript/index).
- Azure Video Indexer output quality: include rich video analysis in markdown (timings, speakers, sentiment/emotion signals, and topic/keyword/context summaries), not transcript-only text blocks.
- Azure Video Indexer fixes: when a working reference client exists in `diwo/`, mirror its proven auth/token + processing-state flow before introducing alternative logic.
- Never introduce test-only abstractions like `IAzureIntegrationSampleResolver` into core production library code.
- Image enrichment tasks: after OCR, send artifacts through shared `IChatClient` prompt constants; produce detailed visual descriptions, Mermaid/tables for diagrams, and MetaMD-compliant markdown.
- Image AI enrichment must reject missing MIME metadata.
- After AI image enrichment, strip legacy/fallback image comments so final markdown has one canonical image placeholder + description.
- Front matter titles must ignore metadata/image-description comments and use first real document text.
- Intelligence helper refactors must return explicit result objects rather than relying on hidden side effects.
- Image placeholders must use markdown image links (`![alt](file.png)`) when persisted files exist; use bold fallback only when no file exists.
- If AI image enrichment returns no insight, log and continue (soft failure).
- Converter selection diagnostics: when a converter throws in `ConvertAsync`, include converter name and detected mime/extension in user-visible failure details.
- Converter failure classification: if provider/converter chain fails with authentication/authorization (`401`/`403` or credential auth errors), surface `FileConversionException` with auth context instead of masking it as `UnsupportedFormatException`.
- Converter-routing tests: assert behavior (selected path/provider and typed exceptions), not brittle message include/exclude string checks.
- Project cleanup/refactor tasks must be executed step-by-step: audit converter groups (agent-assisted when available), apply incremental refactors, then run full regression tests.
- Telemetry changes must instrument both overall document duration and per-page duration with trace + metric coverage.
- For large converters, use partial classes and dedicated subfolders.
- Markdown hygiene: strip non-printable spaces (NBSP/ZWSP/etc.) and replace with ASCII spaces.
- Architecture revamps: prefer DI-first composition, per-request cloud model selection, and `System.IO.Pipelines`-compatible scheduling.
- DOCX processing changes should preserve pipeline parallelism and output ordering.
- URL APIs must expose `Uri` overloads in addition to string forms.
- Manual Azure config defaults: do not auto-populate `AzureIntegrationConfigDefaults` from environment variables.
- Azure Video Indexer config binding should use explicit settings objects/JSON values (including `ArmAccessToken`) and must not inject `AccountName`/resource identifiers from environment-variable fallbacks.
- AzureIntelligence integration tests: do not source ARM tokens from `diwo`; keep Video Indexer auth in explicit `HardcodedVideoIndexerOptions`.
- Azure Video Indexer live tests in this repo must use explicit `HardcodedVideoIndexerOptions` (including `ArmAccessToken`) and must not require Azure CLI / device login / `DefaultAzureCredential`.
- Azure Video Indexer validation must prove server-side indexing: extract `videoId` from transcript metadata and verify `/Videos/{videoId}/Index` reaches `state=Processed` with transcript entries.
- Never use `MemoryStream` for conversion paths; rely on file-based processing.
- Disk-first refactors: shared disk/workspace helpers go to reusable base classes, not nested per-converter helpers.
- Document pipeline changes must align with `docs/DocumentProcessingPipeline.md` and keep shared setup centralized.
- Manual conversion diagnostics must persist output to disk and keep MetaMD image description blocks.
- Multi-page tables must emit continuation comments and populate `table.pageStart`, `table.pageEnd`, `table.pageRange` metadata.
- PDF converters must honor `SegmentOptions.Pdf.TreatPagesAsImages` by rendering pages to PNG, running OCR/vision enrichment, and composing image+recognized-text segments.
- Persist conversion workspaces through `ManagedCode.Storage` with sanitized per-document folders and store extracted artifacts + final markdown there.
- Root path configurability: `MarkItDownPathResolver` must support configurable root via `MarkItDownOptions.RootPath` or `MarkItDownServiceBuilder.UseRootPath()`, with lock-guarded atomic initialization and conflict exceptions.

### Autonomy

- Start work immediately; ask questions only for true blockers not discoverable from code/docs.
- Report status when tasks are complete or when blocked by external dependencies.

### Advisor stance (ALL TASKS)

- Be direct and factual; no fluff.
- Challenge weak assumptions and call out risks.
- If unsure, state uncertainty and propose verification.
- Treat quality and security regressions as blockers.

### Code Style

- Follow `.editorconfig` and existing repository conventions.
- Prefer explicit constants/config over magic literals.
- Keep public APIs documented and errors actionable.

### Critical (NEVER violate)

- Never commit secrets, API keys, or customer data.
- Never mock internal systems in integration tests.
- Never skip tests to force green CI.
- Never force-push to `main`.
- Never approve/merge PRs automatically.

### Boundaries

Always:

- Read `AGENTS.md` and relevant docs before edits.
- Run required verification commands before finalizing.

Ask first:

- Public API contract changes
- New third-party dependencies
- Database/schema changes
- Deleting code files

---

## Preferences

### Likes

- Clear diagnostics and deterministic converter routing.
- Architecture-first implementation with documented flows.

### Dislikes

- Silent fallbacks that hide configuration or provider failures.
- Brittle tests coupled to exact exception text formatting.
