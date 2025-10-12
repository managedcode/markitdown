# Document Processing Pipeline (Disk-First)

This document is the canonical description of how the .NET port processes source documents. It reflects the project rule that **we do not rely on `MemoryStream` buffering for conversion**. Every stage is built around deterministic disk-backed access so large files (think 3 GB PDFs, ZIPs full of media, or multi-gigabyte CSV exports) can flow without blowing up RAM.

---

## 1. Pipeline Overview

| Stage | What Happens | Key Types |
|-------|--------------|-----------|
| 0. Source acquisition | The caller supplies a file path or URI. Remote inputs (HTTP/Blob/Zip entry) are **persisted to disk first**; no in-memory mirroring. | `MarkItDownClient`, `SourceMaterializer` (planned helper) |
| 1. Temp-store orchestration | Large inputs are streamed straight into a temp file managed by the pipeline. Small inputs may stay in shared read-only files, but never in `MemoryStream`. | `StreamPipelineBuffer` (to be retrofitted for disk spill) |
| 1a. Local workspace management | A per-conversion working directory is created under `MarkItDownOptions.Pipeline.TempRoot` (default `%TEMP%/markitdown/{guid}/`). | `TempWorkspaceManager` (planned helper) |
| 2. Format detection | File headers and supplied metadata are inspected via `ManagedCode.MimeTypes.MimeHelper` to build the initial `StreamInfo`. | `MimeHelper`, `ConversionProgress` |
| 3. Converter scheduling | Converters are grouped by priority. Each receives its own `FileStream` (or `RandomAccess` handle) pointing at the persisted file. | `ConverterRegistration`, `DocumentConverterBase` / `DocumentConverterBase` |
| 4. Extraction & enrichment | Winning converter parses the document, emits `DocumentSegment` + `ConversionArtifacts`, and triggers inline AI hooks where applicable. | Format-specific converters, `ConversionContextAccessor` |
| 5. Middleware pass | Global middleware inspects the artifacts/segments. This is where AI chat enrichment, redaction, or telemetry stamping runs. | `ConversionPipeline`, `AiImageEnrichmentMiddleware` |
| 6. Composition | `SegmentMarkdownComposer` produces the final Markdown + title. Telemetry counters, metadata, and performance stats are attached. | `SegmentMarkdownComposer`, `MarkItDownDiagnostics` |

**Never** skip stage 0 (persist to disk) unless the source file already exists on disk. This rule avoids the hidden cost of loading multi-GB payloads into managed memory.

---

## 2. Source & Buffering Strategy

### 2.1 File-first contract
- `MarkItDownClient.Convert*` overloads must either receive a path or write their input stream to a managed temp file before detection.
- A conversion-scoped workspace directory is created inside `MarkItDownOptions.Pipeline.TempRoot` (default `%TEMP%/markitdown/`). A GUID or timestamp + PID keeps directories unique and traceable.
- The persisted source file always lives inside that workspace (`{workspace}/source.dat`) so cleanup is deterministic.
- The temp file must be opened with `FileOptions.Asynchronous | FileOptions.SequentialScan` so we can share handles across converters efficiently.
- Callers that only have a `Stream` (e.g., HTTP response or in-memory ZIP entry) invoke the materialiser helper. The pipeline exposes a utility like:
  ```csharp
  using var workspace = TempWorkspaceManager.Create(options);
  using var materialised = await SourceMaterializer.CreateAsync(stream, streamInfo, workspace, options, cancellationToken);
  FileInfo backingFile = materialised.File;
  ```
- The workspace tracks every additional artifact (extracted ZIP entries, rendered images, AI intermediates). When the conversion completes or fails, `workspace.Dispose()` recursively deletes the directory unless preservation is requested.

### 2.2 Size-aware thresholds
| File Size | Strategy | Rationale |
|-----------|----------|-----------|
| <= 256 MB | Direct file copy; keeps temp usage small but still disk-backed. | Fits easily on SSD; avoids LOH pressure. |
| 256 MB - 5 GB | Always spill to disk; allow concurrent readers via `RandomAccess`. | Common range for scanned PDFs, media archives. |
| > 5 GB | Require explicit opt-in (`ConversionRequest.Pipeline.AllowLargeFiles`). Enforce streaming converters to process incrementally or bail early with a clear error. |

`StreamPipelineBuffer` will be updated to:
- Track origin path vs temp path.
- Store all files inside the active workspace.
- Open read-only handles for each converter without returning `MemoryStream`.
- Report progress (`buffer-disk`, `buffer-complete`) including final size and workspace path (hashed).

### 2.3 Performance guardrails
- Telemetry must log `markitdown.buffer.disk.bytes`, `markitdown.buffer.disk.duration_ms`, and the workspace path (hashed/anonymised).
- Add a `MarkItDownOptions.MaxBufferMegabytes` and refuse conversion when the persisted size exceeds it, unless the caller overrides.
- For ZIP archives, create per-entry temp files inside the workspace. A background cleanup task ensures directories are removed even if the process crashes (e.g., next start deletes stale workspaces older than N hours).
- Provide a `MarkItDownOptions.Pipeline.PreserveWorkspaceOnFailure` flag for debugging; when true, we log the directory path and skip deletion so developers can inspect artifacts.

---

## 3. Converter Families

The table below maps every converter family to its scope and special notes. "Inline AI" means the converter triggers provider calls during extraction. "Middleware" means post-extraction enrichment is required.

| Family | Converters | Typical Inputs | Inline AI | Middleware Needs | Notes |
|--------|------------|----------------|-----------|------------------|-------|
| Document Suite | `PdfConverter`, `DocxConverter`, `PptxConverter`, `EpubConverter`, `Fb2Converter`, `OdtConverter`, `JupyterNotebookConverter`, `DocBookConverter`, `OpmlConverter`, `EndNoteXmlConverter`, `RtfConverter`, `XlsxConverter`, `EmlConverter` | Office docs, structured XML/JSON bundles | PDF/DOCX/PPTX optionally use Document Intelligence & Vision during extraction | `AiImageEnrichmentMiddleware` for images; future redaction middleware for email | Respect page/slide ordering. Large DOCX images must be temp-file backed. |
| Web & Feeds | `HtmlConverter`, `WikipediaConverter`, `BingSerpConverter`, `RssFeedConverter` | HTML pages, SERP snapshots, RSS/Atom feeds | No | Optional link normalisation middleware | Callers must pre-download content; converters assume a file path containing the payload. |
| Markup & Diagrams | `AsciiDocConverter`, `DjotConverter`, `GraphvizConverter`, `LatexConverter`, `OrgConverter`, `PlantUmlConverter`, `RstConverter`, `TextileConverter`, `TikzConverter`, `TypstConverter`, `WikiMarkupConverter`, `MermaidConverter` | Text-based markup, diagrams | No | Diagram converters may rely on rendering middleware | `MermaidConverter` should never render in-memory; run CLI tools against temp files if needed. |
| Data & Metadata | `CsvConverter`, `MetaMdConverter`, `JsonConverter`, `XmlConverter`, `BibTexConverter`, `RisConverter`, `CslJsonConverter` | Tabular or metadata files | No | Optional schema validation middleware | `MetaMdConverter` extracts Markdown metadata blocks and attaches them as segments/artifacts. |
| Media | `ImageConverter`, `AudioConverter`, `YouTubeUrlConverter` | Images, audio files, YouTube URLs | Image & Audio converters call providers inline; YouTube uses metadata APIs | `AiImageEnrichmentMiddleware` for remaining images | Audio transcription must stream from disk; no byte-array mirrors. |
| Archives & Packaging | `ZipConverter`, future TAR/7z | Aggregated content | Depends on entries | Might run child pipelines per entry | Each entry is persisted to its own temp file before being handed to inner converters. |

---

### 3.1 Standard Document Converter Template

The DOCX implementation (`src/MarkItDown/Converters/Documents/Docx/DocxConverter.cs`) is the authoritative example of how disk-first document converters must behave. Every converter in the **Document Suite** row above should mirror the same contract.

#### Base class & construction
- Inherit from `DocumentPipelineConverterBase` (or a specialised subclass such as `WordprocessingDocumentConverterBase`). This grants access to `MaterializeSourceAsync`, workspace helpers, and consistent exception handling.
- Accept optional `SegmentOptions` and `IConversionPipeline` arguments. Store `segmentOptions ?? SegmentOptions.Default` and `pipeline ?? ConversionPipeline.Empty`.
- When the format supports inline image or document-intelligence enrichment, accept the relevant providers (see the DOCX constructor wiring `IImageUnderstandingProvider`, `IDocumentIntelligenceProvider`, and `IAiModelProvider`).

#### `AcceptsInput` / `Accepts` contract
- `AcceptsInput(StreamInfo)` must validate extensions and MIME types using local `HashSet<string>` declarations. Do not touch the file system inside this method.
- `Accepts(Stream, StreamInfo, CancellationToken)` may inspect the leading bytes, but it **must** restore the original stream position in `finally`, exactly like `DocxConverter.Accepts`.

#### ConvertAsync skeleton
```csharp
public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo info, CancellationToken token)
{
    ArtifactWorkspace? workspace = null;
    try
    {
        await using var source = await MaterializeSourceAsync(stream, info, ".ext", token).ConfigureAwait(false);

        var effectiveSegments = ResolveSegmentOptions();              // pull from ConversionContextAccessor when present
        workspace = CreateArtifactWorkspace(info, effectiveSegments); // same pattern as DOCX

        var imagePersistor = new ImageArtifactPersistor(workspace, info); // only when images are emitted
        var extraction = await ExtractAsync(source.FilePath, info, imagePersistor, effectiveSegments, token).ConfigureAwait(false);

        await conversionPipeline.ExecuteAsync(info, extraction.Artifacts, extraction.Segments, token).ConfigureAwait(false);

        var generatedAt = DateTime.UtcNow;
        var titleHint   = ExtractTitle(extraction.RawText);
        var meta        = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, info, effectiveSegments, titleHint, generatedAt);
        var title       = meta.Title ?? titleHint ?? FallbackTitleFrom(info);

        var metadata = BuildDocumentMetadata(title, titleHint, extraction); // always include pages/images/tables

        string MarkdownFactory() =>
            SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, info, effectiveSegments, titleHint, generatedAt).Markdown;

        return DocumentConverterResult.FromFactory(
            MarkdownFactory,
            title,
            extraction.Segments,
            extraction.Artifacts,
            metadata,
            artifactDirectory: workspace.DirectoryPath,
            cleanup: null,
            asyncCleanup: workspace,
            generatedAtUtc: generatedAt);
    }
    catch (Exception ex) when (ex is not MarkItDownException)
    {
        if (workspace is not null)
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
        }
        throw new FileConversionException($"Failed to convert …: {ex.Message}", ex);
    }
    catch
    {
        if (workspace is not null)
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
        }
        throw;
    }
}
```

Key points:
- Always call `MaterializeSourceAsync` **before** reading the format. Never operate on the inbound stream directly.
- Always allocate an `ArtifactWorkspace` and return it through `DocumentConverterResult.FromFactory`. Successful conversions rely on the caller to dispose the workspace; failures must dispose it immediately.
- `conversionPipeline.ExecuteAsync` runs **after** extraction but **before** composing MetaMD so middleware sees the raw artifacts/segments.

#### Extraction result contract
- Expose a private method (for example `ExtractDocumentAsync`) that returns a `List<DocumentSegment>`, a `ConversionArtifacts` instance, and any raw text needed for title detection. DOCX uses a dedicated record type (`DocxExtractionResult`) to keep these values together.
- Segments must live in a mutable `List<DocumentSegment>` so middleware can insert or adjust items.
- Populate `DocumentSegment.AdditionalMetadata` with ordering hints (`MetadataKeys.Page`, `MetadataKeys.Slide`, `MetadataKeys.Sheet`, etc.).
- Build a page accumulator so every original page produces exactly one `DocumentSegment`; when an element (for example a Word table) spans multiple pages emit continuation placeholders rather than duplicating rows.
- Persist images with `ImageArtifactPersistor` and store only relative file paths plus metadata in memory. The enrichment middleware will reopen the files.
- Add table data to `ConversionArtifacts.Tables` and populate per-table metadata (`tableIndex`, `table.pageStart`, `table.pageEnd`, `table.pageRange`) so downstream systems can locate multi-page grids without scanning Markdown comments.
- Emit conversion progress when the format has natural checkpoints. DOCX uses `ReportDocxProgress`; other converters should follow the same pattern to keep diagnostics consistent.

#### Metadata & title handling
- Always stamp `MetadataKeys.DocumentTitle`, `DocumentTitleHint`, `DocumentPages`, `DocumentImages`, and `DocumentTables`. Additional format-specific keys (`epub.author`, `notebook.cells.code`, etc.) should be recorded in `ConversionArtifacts.Metadata` during extraction.
- Derive titles from the first meaningful content when the file lacks explicit metadata (see `DocxConverter.ExtractTitle` for guidance).

#### Error handling
- Wrap `ConvertAsync` in the two-tier `try/catch` structure shown above: unexpected exceptions become `FileConversionException`, and the inner `catch` ensures the workspace is disposed on any path.
- Never swallow `MarkItDownException`; let it propagate so callers receive clear validation failures.

Following these rules keeps converters compatible with middleware, telemetry, AI enrichment, and temp-workspace cleanup. When in doubt, replicate the exact flow from DOCX and adjust only the extraction logic.

---

## 4. Detailed Playbooks

### 4.1 PDF (`PdfConverter`)
1. Open the persisted file with `FileStreamOptions { Access = Read, Share = Read, Options = Asynchronous | SequentialScan }`.
2. Attempt Document Intelligence via the request-scoped provider. The analyser must read straight from the file path.
3. If AI is unavailable, fall back to PdfPig text extraction reading from disk.
4. Render page snapshots through `IPdfImageRenderer`, writing PNGs to temp files when needed (never hold entire base64 strings in managed memory for large documents).
5. Emit `DocumentSegment` per page, attach `TableArtifact` objects, and queue image artifacts for middleware enrichment.
6. Close all additional handles before middleware runs to avoid file locks.

### 4.2 DOCX (`DocxConverter`)
1. Copy the `.docx` zip to a working directory; process parts using `OpenXml` APIs directly from disk.
2. For every image relationship, stream into temp files and enqueue the artifact for AI image enrichment; the middleware processes them sequentially to respect provider limits.
3. Queue provider work (Vision/Document Intelligence) but ensure payloads are streamed into the SDK without loading entire files into memory.
4. Compose Markdown while awaiting image enrichments; placeholders must reference the temp file path so middleware can reopen it if required.
5. When tables flow across page breaks, prepend `<!-- Table spans pages X-Y -->` to the table, add continuation comments (`<!-- Table 4 continues on page 16 (pages 15-16) -->`) on the following pages, and stamp the new table metadata keys so RAG clients can reconcile the Markdown with the source document.
6. Honour `SegmentOptions.Pdf.TreatPagesAsImages` when callers request page rasterisation—render each page to PNG, run it through the configured `IImageUnderstandingProvider` for OCR/vision, and compose the page segment with the placeholder plus the recognized text.

### 4.3 PPTX (`PptxConverter`)
Same principles as DOCX: operate on disk parts, spill images to temp files, and never base64 entire slide decks in memory.

### 4.4 HTML & Wikipedia (`HtmlConverter`, `WikipediaConverter`, `BingSerpConverter`)
- Assumes the caller already saved the HTML response to disk.
- Use `HtmlContentLoader.ReadHtmlAsync` in streaming mode (to be refactored to avoid `MemoryStream`).
- Preserve canonical URLs and metadata in `ConversionArtifacts`.

### 4.5 CSV & Data (`CsvConverter`, `MetaMdConverter`, etc.)
- Build `Sep.Reader` pipelines around `FileStream` so multi-GB CSVs are streamed row-by-row.
- `MetaMdConverter` must read the file sequentially, detect metadata fences, emit segments, and avoid buffering entire Markdown blocks when not required. Large posts should stream line-by-line.
- JSON/XML converters should leverage `System.Text.Json`/`XmlReader` with `FileStream` backing to maintain streaming semantics.

### 4.6 Markup/Diagram Converters
- If a tool chain (Graphviz, PlantUML, Mermaid CLI) is needed, hand it the temp file path.
- Collect rendered output as additional artifacts stored on disk until composition.

### 4.7 Media (`ImageConverter`, `AudioConverter`)
- Open files directly. EXIF extraction should run on the file path (ExifTool CLI) rather than copying to `MemoryStream`.
- Audio transcription providers must receive the temp file path or a forward-only stream created from it; do not pre-buffer audio samples into arrays.

### 4.8 Archives (`ZipConverter`)
- Extract each entry into `{workspace}/entries/{index}/{fileName}`. Do **not** buffer entries into memory even if they're small; the extraction helper can short-circuit for tiny files but defaults to disk.
- Pass the temp file path plus derived `StreamInfo` to the inner converter pipeline.
- After each entry is processed, delete its directory unless `PreserveWorkspaceOnFailure` is set. This keeps disk usage bounded during large archives.
- If the archive contains nested archives, recurse by creating subdirectories under the entry folder to keep provenance clear.

---

## 5. AI Enrichment

| Hook | Trigger | Requirements |
|------|---------|--------------|
| Document Intelligence | Enabled via `SegmentOptions.Image.EnableDocumentIntelligence` and provider availability | Providers read from disk; ensure temporary copies are seekable and cleaned up |
| Image Understanding | Runs inside `DocxConverter`, `PptxConverter`, `PdfConverter`, `ImageConverter` | Vision SDKs should operate on temp files or streaming responses; capture OCR text/captions without retaining huge byte arrays |
| Chat Enrichment | `AiImageEnrichmentMiddleware` after extraction | Middleware reopens the image temp file if extra context is needed; injects insights into segments |
| Media Transcription | `AudioConverter` or future video handlers | Feed providers with file handles; spool transcripts to disk if they exceed in-memory thresholds |

All AI hooks must record telemetry (`AiInputTokens`, `AiOutputTokens`, `AiModelId`) and include the workspace strategy in diagnostic logs for auditing.

---

## 6. Performance & Resilience Checklist

- [ ] Large file guard (`MaxBufferMegabytes`) enforces safe defaults.
- [ ] Telemetry spans include buffer size and duration.
- [ ] Temp files are created under a dedicated root (`MarkItDownOptions.Pipeline.TempRoot`, default `%TEMP%/markitdown/`) with unique names per conversion; cleaned up deterministically and optionally preserved on failure when requested.
- [ ] Each converter documents its own memory behaviour in XML comments so contributors keep the disk-first policy in mind.
- [ ] Tests add fixtures for large-file scenarios (mocked with sparse files) to verify no `OutOfMemoryException` occurs.
- [ ] Manual docs instruct CLI users to provide file paths and avoid piping giant blobs via standard input.

---

## 7. References

| Item | Path |
|------|------|
| Core client orchestration | `src/MarkItDown/MarkItDownClient.cs` |
| Buffering infrastructure (to be refactored for disk spill) | `src/MarkItDown/Conversion/Pipelines/StreamPipelineBuffer.cs` |
| Converter base helpers | `src/MarkItDown/Converters/DocumentConverterBase.cs`, `src/MarkItDown/Converters/Base/StructuredXmlConverterBase.cs` |
| Representative converters | `src/MarkItDown/Converters/Documents/*.cs`, `src/MarkItDown/Converters/Data/*.cs`, `src/MarkItDown/Converters/Markup/*.cs`, `src/MarkItDown/Converters/Media/*.cs`, `src/MarkItDown/Converters/Web/*.cs` |
| Middleware | `src/MarkItDown/Conversion/Middleware/AiImageEnrichmentMiddleware.cs` |
| Telemetry helpers | `src/MarkItDown/MarkItDownDiagnostics.cs`, `src/MarkItDown/TelemetryTags.cs` |

This document should be treated as a living spec. Any future converter, middleware component, or buffering change must stay aligned with the disk-first rule and update this reference accordingly.
