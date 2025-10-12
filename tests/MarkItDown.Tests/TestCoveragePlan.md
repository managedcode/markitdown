# MarkItDown .NET Test Expansion Plan

## 1. Source Assets

The upstream Python project (`microsoft-markitdown/packages/markitdown/tests/test_files`) ships a comprehensive set of fixtures covering HTML, Office, audio, archive, notebook, RSS, SERP, image, and binary formats. These files are now mirrored in this repository under `tests/MarkItDown.Tests/TestFiles/` for reuse in .NET tests.

| File | Format | Primary Expectations |
|------|--------|---------------------|
| `autogen-paper.docx`, `autogen-paper-with-comments.docx` | Word | Heading detection, inline images skipped, comments hidden |
| `autogen-forecast.xlsx`, `legacy-ledger.xls` | Excel | Worksheet headings, table conversion |
| `autogen-strategy.pptx` | PowerPoint | Slide text, chart captions, embedded images |
| `autogen-trial-transcript.pdf` | PDF | Multi-page text extraction |
| `autogen-ebook.epub` | EPUB | Spine ordering, metadata parsing |
| `autogen-blog.html`, `microsoft-wikipedia.html`, `bing-search-results.html` | HTML/Serp | Sanitised content, link normalisation |
| `microsoft-blog-feed.xml`, `kanji-dataset.csv`, `customer-records.json`, `autogen-notebook.ipynb` | XML/CSV/JSON/Notebook | Charset handling, header rendering, code fences |
| `mixed-fixture-archive.zip` | Archive | Delegated inner conversions |
| `meeting-audio.mp3`, `meeting-audio.m4a`, `meeting-audio.wav` | Audio | Metadata extraction, transcript wiring |
| `project-status.msg` | Outlook message | Envelope fields, body markdown |
| `equations.docx` | Math-heavy Word | Equation formatting |
| `architecture-diagram.jpg`, `llm-workflow.jpg` | Images | OCR/exif-paths |
| `random.bin` | Negative case for unsupported formats |

These fixtures enable parity testing across nearly every built-in converter.

## 2. Current Gaps

- Integration coverage only touches a handful of formats with synthetic streams.
- Converters that depend on external tools (`Docx`, `Epub`, `BingSerp`, `Image`, `Audio`, `Pdf`, `Zip`) either lack tests or rely on network/process access and therefore remain untested.
- `StreamInfoGuesser` and `MarkItDown.ConvertUriAsync` paths are partially covered but do not exercise real-world file signatures or data URIs.
- Regression checks that specific IDs/phrases appear (mirroring `_test_vectors.py`) are missing.

## 3. Target Architecture for Tests

To reach ≥90% line coverage without shipping native/tooling dependencies, introduce seams so the converters can run against the local fixtures deterministically:

1. **Dependency Abstractions** *(in progress)*
   - Audio: wrap exiftool + transcription (done).
   - PDF: wrap PdfPig + PDFtoImage (done).
   - HTML/Wikipedia/Bing: abstract HTTP fetchers to fake responses.
   - Image: wrap OCR/captioner/exif extraction.
   - Office formats: expose document openers (e.g., interface for `Docx` package) to allow fakes if needed.

2. **Fixture Loader Utilities**
   - Create a shared helper in tests to open assets by name, returning byte arrays/streams and expected metadata.
   - Port `_test_vectors.py` into a C# `TestVector` class with `MustInclude`/`MustNotInclude` lists (values copied directly).

3. **Parameterized Integration Tests**
   - Build an xUnit `[Theory]` over the converted test vectors for: file path, byte stream + `StreamInfo`, bare stream (guessing), `file://`, and `data:` URIs.
   - For HTTP coverage, fake an `HttpMessageHandler` that serves the bytes from `TestFiles` to avoid live network traffic.

4. **Converter-Specific Unit Tests**
   - Docx/Xlsx/Pptx: verify headings/tables, ensure inline images yield base64 placeholders not raw binaries.
   - JSON/Notebook/RSS/CSV: assert formatting decisions (code fences, table layout, header levels).
   - Zip: use `mixed-fixture-archive.zip` to confirm recursive conversion and MIME detection.
   - Audio/Image: with injected fakes, validate metadata layout and optional transcript/caption text.

5. **Error & Edge Cases**
   - Use `random.bin` to assert `UnsupportedFormatException` is thrown.
   - Ensure corrupted archives or missing metadata gracefully fall back with defaults.

6. **Coverage Measurement**
   - Keep `dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"` as the baseline.
   - Add a CI step that parses `coverage.cobertura.xml` and fails if line-rate < 0.90 once the suite is complete.

## 4. Step-by-Step Execution Plan

| Step | Description | Output |
|------|-------------|--------|
| 1 | Port `_test_vectors.py` to `TestVector.cs`, copying `MustInclude`/`MustNotInclude` data. | Strongly typed dataset reused across tests. |
| 2 | Implement `TestAssetLoader` in tests to read bytes/streams from `TestFiles`. | Simplifies fixture access and keeps stream resets consistent. |
| 3 | Extend `MarkItDownIntegrationTests` with `[Theory]` for: local file, seekable stream, non-seekable stream, `file://`, `data:` URIs using test vectors. | Broad coverage of public APIs & MIME guessing. |
| 4 | Introduce `HttpClient` fake (e.g., `StubHttpMessageHandler`) and cover `ConvertFromUrlAsync` without real network. | Exercises HTTP path deterministically. |
| 5 | Add converter-specific tests mirroring Python suite: Docx/Epub/Pptx/Xlsx/Pdf/Audio/Image/Zip. Use dependency injection seams (audio/pdf done; add others). | High-confidence verification of each converter’s output. |
| 6 | Cover negative paths: unknown MIME (`random.bin`), cancelled tokens, oversized entries. | Ensures exception coverage. |
| 7 | Run coverage, iterate on gaps (inspect Cobertura per-class metrics). | Achieve ≥90% line coverage. |

## 5. Immediate Actions Completed

- Copied upstream fixtures into `tests/MarkItDown.Tests/TestFiles/`.
- Added dependency seams plus targeted unit tests for Audio and PDF converters.
- Raised overall line coverage to ~44% (from ~41%).
- Landed fixtures and regression tests for DocBook, JATS, OPML, FB2, ODT, citation formats (BibTeX/RIS/EndNote/CSL JSON), plain-text markups (LaTeX/rST/AsciiDoc/Org/Djot/Typst/Textile/Wiki), diagram syntaxes (Mermaid/Graphviz/PlantUML/TikZ), TSV tables, and the MetaMD profile.

## 6. Next Deliverables

1. Port test vectors to C# and back them with the copied fixtures.
2. Author comprehensive integration tests (Step 3) to validate core conversion paths.
3. Continue abstracting external dependencies (HTML fetchers, exif/ocr) so additional converters can be exercised without native tooling.

Tracking progress against this plan and measuring coverage after each wave will move the suite toward the 90% goal while staying deterministic and toolchain-independent.
