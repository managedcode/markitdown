# Development and CI

This project already has CI pipelines under `.github/workflows/`.

## Workflows

- `ci.yml` — restore, build, test, package artifacts for pushes/PRs.
- `release.yml` — release flow for main branch with package publishing.

## Local Quality Gate

Run these before opening a PR:

1. `dotnet restore MarkItDown.slnx`
2. `dotnet build MarkItDown.slnx`
3. `dotnet test MarkItDown.slnx`
4. `dotnet format MarkItDown.slnx`

Optional coverage:

5. `dotnet test MarkItDown.slnx --collect:"XPlat Code Coverage"`

## Static Analysis

- .NET analyzers are enabled globally (`Directory.Build.props`).
- Warnings are treated as errors, so analyzer violations fail the build.

## Notes for Live Integration Suites

- Some tests require external cloud credentials and may fail when auth tokens are expired or unavailable.
- Live failures should not be swallowed; they must expose the real provider/auth/network error.
- Both build and release workflows run the automated suite with `--filter "Category!=Manual"`. Manual suites require explicit local fixtures and Azure configuration and run separately; see [manual suite prerequisites](../Testing/Overview.md#manual-suite-prerequisites).
- The original specimen PDF/DOCX must be available under `TestFiles` before build so the generated asset catalog includes them. Missing Azure configuration or specimen fixtures keeps the full-suite check failing; native PDF rendering fixes do not waive these prerequisites.

## Linux PDF native dependencies

`SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies` each declare the literal version `4.151.2` in `Directory.Packages.props`. Reference the Linux native package in the library itself so NuGet consumers receive the compatible runtime too. Depending only on PDFtoImage's older transitive native version can produce a managed/native ABI mismatch even though restore and compilation succeed. `PdfNativeRenderingTests.RenderRealPdf_EncodesPageAsPng` exercises actual PDFium/Skia rendering on the test runner.
