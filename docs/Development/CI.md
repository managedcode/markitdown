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
