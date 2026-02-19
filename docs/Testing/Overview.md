# Testing Overview

This repository follows integration-first testing for conversion behavior.

## Principles

- Prefer real conversion flows over isolated mocks.
- Keep fixture assets in `tests/MarkItDown.Tests/TestFiles/` and reference them through generated `TestAssetCatalog` constants.
- Treat `ManualConversionDebugTests` as required in full-suite runs.
- Live provider tests must fail loudly on real provider/auth/network errors.

## Test Projects

- `tests/MarkItDown.Tests` — core library tests (unit/integration/live/manual)
- `tests/MarkItDown.Cli.Tests` — CLI behavior tests

## Standard Commands

- Restore: `dotnet restore MarkItDown.slnx`
- Build: `dotnet build MarkItDown.slnx`
- Full tests: `dotnet test MarkItDown.slnx`
- Coverage: `dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"`

## Key Suites

- Routing + acceptance: `ConverterAcceptanceTests`, `StreamInfoDetectionTests`
- Converter behavior: `DocxConverterTests`, `PdfConverterTests`, `AudioConverterTests`
- Intelligence/enrichment: `ImageChatEnricherTests`, provider integration suites
- Manual diagnostics: `ManualConversionDebugTests`

## CI Expectations

- CI runs build + tests for PR/push verification.
- Analyzer warnings are treated as errors through `Directory.Build.props`.
