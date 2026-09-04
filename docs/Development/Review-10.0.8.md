# Patch 10.0.8 review — 2026-09-04

## Scope

Review the conversion pipeline, document/data/archive/web converters, intelligence integration, current NuGet dependencies, and upstream changes. Update existing dependencies to their latest stable releases and increment 10.0.7 to 10.0.8. Preserve public API signatures and the existing provider model.

New provider services, a new ingestion adapter package, public API redesign, and NuGet publication are outside this patch. Commit/push is requested; release readiness additionally requires the mandatory manual tests to execute with real prerequisites.

## Upstream assessment

- Microsoft MarkItDown was checked at [4459ed01155f](https://github.com/microsoft/markitdown/tree/4459ed01155f8e1a7ac007f441d34f0422746191), including [v0.1.8b1](https://github.com/microsoft/markitdown/releases/tag/v0.1.8b1), published September 4, 2026. The latest stable release is v0.1.7. Applicable patch fixes include Atom HTML/XHTML text constructs and HTML strikethrough; these now have .NET regression coverage. Existing CSV code already handles BOM, escaped pipes, and line breaks. Python-specific ZIP filename validation and OMML implementation fixes should not be copied into unrelated .NET libraries without a failing .NET case.
- Upstream also added [Azure Content Understanding](https://github.com/microsoft/markitdown/pull/1865) and an [OCR plugin](https://github.com/microsoft/markitdown/tree/4459ed01155f8e1a7ac007f441d34f0422746191/packages/markitdown-ocr). This project already has document OCR, image enrichment, and Azure Video Indexer integration. Content Understanding would require explicit provider configuration, API decisions and live validation; it is a separate optional-provider feature, not a silent replacement for current video routing.
- [CommunityToolkit/AI at 215a5bad3307](https://github.com/CommunityToolkit/AI/tree/215a5bad3307aaafe45ba2ee9e40c071f5f7ce2c) contains VectorData connectors, not an ingestion pipeline. No vector database dependency is needed in this conversion library.
- Ingestion currently lives in [dotnet/extensions at 47b6b06c811f](https://github.com/dotnet/extensions/tree/47b6b06c811fbdb57250d4126f386cfde3f6e8b2/src/Libraries/Microsoft.Extensions.DataIngestion). Its [MarkItDown reader](https://github.com/dotnet/extensions/blob/47b6b06c811fbdb57250d4126f386cfde3f6e8b2/src/Libraries/Microsoft.Extensions.DataIngestion.MarkItDown/README.md) invokes the Python executable or MCP, not ManagedCode.MarkItDown. A future optional native adapter can map ordered segments, page numbers and artifacts into `IngestionDocumentReader` output while keeping preview ingestion dependencies out of core.

## Dependency audit

All 40 centrally declared versions were checked against the NuGet v3 flat-container index. 32 were updated; the other eight already use their latest stable release. `dotnet list MarkItDown.slnx package --outdated --format json` reports no outdated direct references. Vulnerability inspection includes transitive packages and reports none after the upgrade. Baseline restore failed for AngleSharp GHSA-pgww-w46g-26qg and SSH.NET GHSA-q939-rpr3-3284; updates resolve both without suppressing NuGet auditing.

| Package | Previous | Current |
| --- | --- | --- |
| AngleSharp | 1.4.0 | 1.7.3 |
| AWSSDK.Rekognition | 4.0.3.13 | 4.0.100.12 |
| AWSSDK.S3 | 4.0.18.6 | 4.0.102.5 |
| AWSSDK.Textract | 4.0.3.13 | 4.0.100.12 |
| AWSSDK.TranscribeService | 4.0.5.3 | 4.0.101.1 |
| Azure.AI.FormRecognizer | 4.1.0 | 4.1.0 |
| Azure.AI.OpenAI | 2.1.0 | 2.1.0 |
| Azure.AI.Vision.ImageAnalysis | 1.0.0 | 1.0.0 |
| Azure.Storage.Blobs | 12.27.0 | 12.29.2 |
| Azure.Identity | 1.17.1 | 1.21.0 |
| coverlet.collector | 8.0.0 | 10.0.1 |
| DocumentFormat.OpenXml | 3.4.1 | 3.5.1 |
| DotNet.ReproducibleBuilds | 1.2.25 | 2.0.5 |
| Google.Cloud.DocumentAI.V1 | 3.23.0 | 3.25.0 |
| Google.Cloud.Speech.V1 | 3.9.0 | 3.9.0 |
| Google.Cloud.Vision.V1 | 3.8.0 | 3.8.0 |
| ManagedCode.MimeTypes | 10.0.1 | 10.1.1 |
| ManagedCode.Storage.Aws | 10.0.2 | 10.0.7 |
| ManagedCode.Storage.Azure | 10.0.2 | 10.0.7 |
| ManagedCode.Storage.Core | 10.0.2 | 10.0.7 |
| ManagedCode.Storage.FileSystem | 10.0.2 | 10.0.7 |
| ManagedCode.Storage.Gcp | 10.0.2 | 10.0.7 |
| Microsoft.Extensions.AI | 10.3.0 | 10.9.0 |
| Microsoft.Extensions.AI.OpenAI | 10.3.0 | 10.9.0 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.3 | 10.0.11 |
| Microsoft.Extensions.Logging.Abstractions | 10.0.3 | 10.0.11 |
| Microsoft.Extensions.Options | 10.0.3 | 10.0.11 |
| Microsoft.NET.Test.Sdk | 18.0.1 | 18.9.0 |
| MimeKit | 4.15.1 | 4.17.0 |
| Moq | 4.20.72 | 4.20.72 |
| PdfPig | 0.1.13 | 0.1.16 |
| PDFtoImage | 5.2.0 | 5.4.0 |
| Sep | 0.12.2 | 0.17.0 |
| Shouldly | 4.3.0 | 4.3.0 |
| Testcontainers.Azurite | 4.10.0 | 4.14.0 |
| SkiaSharp | 3.119.2 | 4.151.2 |
| Spectre.Console | 0.54.0 | 0.57.2 |
| YoutubeExplode | 6.5.7 | 6.6.2 |
| xunit | 2.9.3 | 2.9.3 |
| xunit.runner.visualstudio | 3.1.5 | 4.0.0 |

## Confirmed defects and verification checklist

- [x] EPUB namespace handling returned empty books; title and metadata keys were also inconsistent. Regression coverage uses the existing real EPUB plus container variants and reversed spine order.
- [x] DOCX table conversion populated ordinary blank cells from prior rows. Preserve explicit vertical merges without fabricating values for unmerged cells.
- [x] XLSX multiline cells split Markdown table rows. Normalize CRLF, CR and LF to `<br />`, including shared and inline strings.
- [x] Atom HTML/XHTML content lost formatting. Render markup by its declared type and preserve literal text constructs.
- [x] HTML `s`, `del`, `strike` and inline line-through styles lost deletion semantics.
- [x] HTTP downloads used full response buffering before disk materialization. Use response headers completion and test a response above HttpClient's buffer limit.
- [x] Caller cancellation could become unsupported-format errors. Preserve it through routing and middleware.
- [x] Regex image replacement interpreted literal dollar tokens in AI descriptions. Use a match evaluator and verify the exact description block.
- [x] ManagedCode.Storage 10.0.7 string upload references the removed MimeHelper TEXT property accessor in MimeTypes 10.1.1. Upload persisted UTF-8 files through the FileInfo overload; real Azurite tests verify content, MIME, local copy and deletion.
- [x] Generated asset task emitted unreachable-code warnings from a redundant return. Let the fragment task factory generate its return.
- [x] Manual tests reported success without Azure configuration or missing original specimen assets. Fail with actionable prerequisite errors; do not fabricate image descriptions.
- [ ] Complete mandatory live/manual tests after supplying real Azure configuration and original specimen PDF/DOCX.

## Review and test cycles

1. Baseline/dependency audit: original build failed NuGet vulnerability checks. After upgrades, build succeeded; full tests found the storage ABI issue (416 passed, two failed, two pre-existing live skips in core; three CLI tests passed). Added regression tests reproduced converter/routing/web bugs. Full red run: 414 passed, 26 failed, two skipped in core, three CLI passed. This run includes six newly exposed manual prerequisite failures.
2. Implementation and regression review: build succeeded with zero warnings/errors; full core suite: 434 passed, seven failed, two skipped; CLI: three passed. Six failures are missing manual prerequisites; one DOCX test needed to compare parsed row cells because the Markdown composer normalizes repeated spaces. Exact empty-cell artifact assertions remain intact. Follow-up: 442 passed, eight failed, two skipped in core; the eight were six prerequisites plus two newly reproduced ZIP cancellation defects, now fixed.
3. Independent final review found styled HTML losing link/heading/paragraph semantics, prefixed XHTML losing formatting, and ignored CSS declaration overrides. New tests reproduced all five failing cases. After fixes, formatting and format verification succeed; Debug build and explicit analyzer build each report zero warnings/errors. Full coverage run: 450 passed, six failed, two skipped in core; three CLI tests passed. All added product regression tests pass. Core line coverage is 69.50%, branch coverage 56.01%; missing manual prerequisites limit live coverage.
4. Release build and full regression: zero build warnings/errors; 450 core and three CLI tests passed, the same six manual prerequisite failures and two pre-existing live skips remain. Release package 10.0.8 is generated locally; nothing has been published to NuGet.

All full-suite runs include `ManualConversionDebugTests`. Existing upstream-dependent YouTube/Video Indexer skips are reported separately and are not evidence of live success. No missing manual prerequisites are converted into skips or passing returns.

## Remaining prerequisites and delivery

The six remaining failures are mandatory and block claiming a green suite or release readiness:

| Test | Missing prerequisite |
| --- | --- |
| `ManualConversionDebugTests.ConvertAndDumpSegmentsAsync` | Explicit Azure OpenAI chat configuration |
| `ManualConversionDebugTests.SpecimenManual_PdfAndDocx_ProduceComparableOutput` | Azure configuration; original paired specimen files also required |
| `ManualConversionDebugTests.SpecimenManual_PdfAsImages_RunsFullDocumentOcr` | Azure chat/OCR configuration; original specimen PDF also required |
| `ManualConversionDebugTests.SpecimenManual_PdfStructured_WithImageRecognition` | Azure chat/OCR configuration; original specimen PDF also required |
| `SpecimenManualParityTests.PdfAndDocx_ProduceMatchingTextAndTables` | Original specimen PDF/DOCX in generated asset catalog |
| `SpecimenManualParityTests.PdfAndDocx_EndToEndParity` | Original specimen PDF/DOCX in generated asset catalog |

The checked-in Azure default is empty and no explicit Azure integration environment configuration was present. Original paired specimen assets are absent from this checkout. Do not substitute synthetic content or unrelated PDFs for these tests. Supply the explicit configuration path and original paired fixtures to finish the goal. Existing YouTube download and Video Indexer live skips remain unverified external cases.

Changes are delivered on `codex/review-10.0.8` while these required checks remain blocked. No PR is merged and no release is claimed. Local logs, TRX and coverage evidence are under the ignored `artifacts/review-10.0.8/` directory; they are not part of the NuGet package or source commit.
