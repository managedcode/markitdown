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

## Manual suite prerequisites

`ManualConversionDebugTests` requires an Azure OpenAI chat deployment and the Azure image/document providers used by its OCR scenarios. Supply explicit integration JSON through `AZURE_INTELLIGENCE_CONFIG_PATH` or `AZURE_INTELLIGENCE_CONFIG_JSON`; the checked-in manual defaults intentionally contain no credentials. Missing configuration and provider errors fail the tests.

The specimen scenarios in `ManualConversionDebugTests` and `SpecimenManualParityTests` additionally require the original specimen submission manual PDF and DOCX under `TestFiles`, generating catalog entries `TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualPdf` and `TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualDocx`. A missing catalog entry or fixture fails with an actionable prerequisite error rather than passing without conversion. Do not replace these documents with unrelated samples to satisfy the specimen-specific assertions.

Manual diagnostics persist their MetaMD output under the test output directory's `ManualOutputs` folder and verify persisted images, image description comments, ordered segments, and scenario-specific conversion results.

## CI Expectations

- CI runs build + tests for PR/push verification.
- Analyzer warnings are treated as errors through `Directory.Build.props`.
