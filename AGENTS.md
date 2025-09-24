# Conversations
any resulting updates to agents.md should go under the section "## Rules to follow"
When you see a convincing argument from me on how to solve or do something. add a summary for this in agents.md. so you learn what I want over time.
If I say any of the following point, you do this: add the context to agents.md, and associate this with a specific type of task.
if I say "never do x" in some way.
if I say "always do x" in some way.
if I say "the process is x" in some way.
If I tell you to remember something, you do the same, update


## Rules to follow
- MIME handling: always use `ManagedCode.MimeTypes` for MIME constants, lookups, and validation logic.

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
