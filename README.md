# MarkItDown for .NET

MarkItDown is the high-fidelity C# port of Microsoft's MarkItDown project. It converts heterogeneous documents into clean, AI-friendly Markdown while preserving structure, metadata, and enrichment hooks. The .NET implementation embraces disk-first processing, non-seekable stream support, and the MetaMD convention documented in `docs/MetaMD.md`.

## Highlights

- **Disk-first pipeline** – every conversion materialises to disk through `DiskBufferHandle` and the updated `StreamPipelineBuffer`. Non-seekable streams (HTTP downloads, pipes, gRPC bodies) are now supported without `MemoryStream` fallbacks.
- **MetaMD output** – Markdown includes workspace metadata, persisted image artifacts, OCR text, and continuation markers exactly as specified in `docs/MetaMD.md` and `docs/MetaMD-Examples.md`.
- **Extensible AI providers** – Azure, AWS, Google, and custom intelligence providers can be toggled per request via `ConversionRequest`; the pipeline avoids test-only abstractions and honours caller overrides.
- **Interactive CLI with progress** – the redesigned CLI shows per-file percentage completion, detailed summaries, and honours the new disk-backed pipeline.

## Quick Start

Install the NuGet package:

```bash
dotnet add package ManagedCode.MarkItDown
```

Convert a file from disk:

```csharp
using MarkItDown;

var client = new MarkItDownClient();
var result = await client.ConvertAsync("sample.pdf");

Console.WriteLine(result.Markdown);
```

Convert a non-seekable stream (for example, an HTTP response) – the client now buffers the payload to disk automatically:

```csharp
await using var httpStream = await httpClient.GetStreamAsync(url, cancellationToken);
var streamInfo = new StreamInfo(extension: ".html", url: url);

var markdown = await client.ConvertAsync(httpStream, streamInfo, cancellationToken);
```

### CLI

Run the interactive CLI against any document. Progress rows now surface percentage complete and a success rate summary:

```bash
dotnet run --project src/MarkItDown.Cli
```

The CLI writes converted Markdown files, renders radar charts for detected formats, and reports `Completed: 8/8 succeeded (100.0%), 0 failed.` style summaries.

## MetaMD at a Glance

The MetaMD format extends Markdown with explicit metadata blocks, workspace paths, and enriched captions. A typical heading emitted by the converter looks like:

```markdown
---
title: Riverside University Health System
source: CLI.CST.MAN.001 V5 - SPECIMEN SUBMISSION MANUAL.docx
workspace.directory: /var/tmp/markitdown/workspace/3e2c8c
workspace.markdownFile: /var/tmp/markitdown/workspace/3e2c8c/document.md
---

![Dashboard Screenshot](dashboard.png)

<!-- Image description:
Bar chart comparing Q3 vs Q4 revenue by product line…
-->
```

Every extractor honours the rules from `docs/MetaMD.md`: segments stay in source order, image metadata is persisted alongside the Markdown, multi-page tables emit `<!-- Table spans pages X-Y -->` notes, and image placeholders always point at on-disk artifacts.

## Architecture Notes

- **DocumentPipelineConverterBase** – format-specific converters (PDF, DOCX, EPUB, audio, images, etc.) now inherit from this base to persist inputs via `MaterializeSourceAsync` and operate on real files.
- **StreamPipelineBuffer** – reimplemented as a disk-backed buffer that exposes fresh read-only streams for each converter attempt, replacing the previous in-memory `ReadOnlySequenceStream` approach.
- **DiskBufferHandle** – utility used throughout the pipeline and intelligence providers to guarantee disk persistence, coordinate cleanup, and avoid silent fallbacks to `MemoryStream`.
- **Providers** – Azure, AWS, and Google analyzers now open file streams directly and only use in-memory buffers where SDKs mandate it (for example, Rekognition's `Image.Bytes`).

For a deeper walk-through of the conversion pipeline, see `docs/DocumentProcessingPipeline.md`.

## Testing & Coverage

Execute the full test suite (CLI + library) and collect coverage:

```bash
dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"
```

The latest run (post disk-first refactor) reports:

- Line coverage: **66.9%**
- Branch coverage: **55.4%**

Coverage artifacts are written under `tests/MarkItDown.Tests/TestResults/` for further analysis.

## Documentation & Resources

- [`docs/MetaMD.md`](docs/MetaMD.md) – formal MetaMD specification and rules.
- [`docs/MetaMD-Examples.md`](docs/MetaMD-Examples.md) – side-by-side examples for images, audio, video, and multi-page tables.
- [`docs/DocumentProcessingPipeline.md`](docs/DocumentProcessingPipeline.md) – high-level architecture of the conversion flow.
- [`docs`](docs) directory – additional notes on pipeline behaviour, AI enrichment, and manual diagnostics.

## License

MarkItDown is released under the [MIT License](LICENSE).
