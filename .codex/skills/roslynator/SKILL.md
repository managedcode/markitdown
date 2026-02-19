---
name: roslynator
description: Run C# static analysis, auto-fix code issues, format code, find unused code, and enforce coding standards in .NET projects. Use when the user asks about code quality, linting, static analysis, code cleanup, unused code, or formatting in C# / .NET projects.
argument-hint: "[solution or project path]"
---

## Prerequisites

Check if roslynator is installed:

```
which roslynator
```

If not found, try running via `dnx` (no install needed, .NET 10+):

```
dnx Roslynator.DotNet.Cli [command] [options]
```

Or install globally:

```
dotnet tool install -g roslynator.dotnet.cli
```

## About Roslynator

Roslynator is a set of code analysis tools for C# powered by Roslyn. It provides 500+ analyzers, refactorings, and code fixes. The CLI tool can analyze, fix, and format entire solutions from the command line — no IDE needed.

The CLI itself contains no analyzers — they come from NuGet packages referenced in your project (e.g. `Roslynator.Analyzers`) or via `--analyzer-assemblies`.

## Common Usage

**Fix all diagnostics in a solution:**
```
roslynator fix MySolution.sln
```

**Fix all diagnostics in a project:**
```
roslynator fix MyProject.csproj
```

**Analyze without fixing (report only):**
```
roslynator analyze MySolution.sln
```

**Format code:**
```
roslynator format MySolution.sln
```

**Find unused code (dead code detection):**
```
roslynator find-unused MySolution.sln
```

**List symbols:**
```
roslynator list-symbols MySolution.sln
```

**Count lines of code:**
```
roslynator lloc MySolution.sln
```

## Commands

| Command | Purpose |
|---------|---------|
| `fix` | Auto-fix diagnostics in project/solution |
| `analyze` | Report diagnostics without fixing |
| `format` | Format whitespace |
| `find-unused` | Find unused declarations (dead code) |
| `list-symbols` | List types, members, and symbols |
| `lloc` | Count logical lines of code |
| `loc` | Count physical lines of code |
| `spellcheck` | Check spelling in comments and strings |

## Key Flags (shared across commands)

| Flag | Purpose |
|------|---------|
| `--projects <name>` | Only process named projects |
| `--ignored-projects <name>` | Skip specific projects |
| `--include <glob>` | Include matching files/folders |
| `--exclude <glob>` | Exclude matching files/folders |
| `-v, --verbosity <level>` | Output level: quiet, minimal, normal, detailed, diagnostic |
| `-p, --properties <NAME=VALUE>` | MSBuild properties (e.g. `Configuration=Release`) |
| `-m, --msbuild-path <dir>` | Path to MSBuild directory |
| `--language <lang>` | Language: `cs` or `vb` |
| `-g, --include-generated-code` | Include generated code |
| `--file-log <path>` | Write output to file |
| `-h, --help` | Show help |

## Analyze-specific Flags

| Flag | Purpose |
|------|---------|
| `-a, --analyzer-assemblies <path>` | Paths to additional analyzer assemblies |
| `--supported-diagnostics <id>` | Report only these diagnostic IDs |
| `--ignored-diagnostics <id>` | Skip these diagnostic IDs |
| `--severity-level <level>` | Minimum severity: hidden, info, warning, error |
| `--ignore-compiler-diagnostics` | Hide compiler messages |
| `-o, --output <path>` | Save diagnostics to file |
| `--output-format` | Report format: xml or gitlab |
| `--execution-time` | Measure analyzer performance |

## Configuration

Roslynator is configured via `.editorconfig` in your project:

**Set severity for all Roslynator analyzers:**
```ini
[*.cs]
dotnet_analyzer_diagnostic.category-roslynator.severity = warning
```

**Enable/disable specific analyzers:**
```ini
[*.cs]
dotnet_diagnostic.RCS1001.severity = none        # Disable specific rule
dotnet_diagnostic.RCS1036.severity = error        # Upgrade to error
```

**Enable/disable refactorings:**
```ini
[*.cs]
roslynator_refactoring.add_braces.enabled = false
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success — no diagnostics found or all fixed |
| `1` | Diagnostics found or not all fixed |
| `2` | Error or execution canceled |

## Workflow with pre-pr

Roslynator is used in the `/pre-pr` quality gate as the first step:

1. `roslynator fix` — auto-fix code issues
2. `dotnet build` — verify compilation
3. `dotnet test` — run tests
4. `quickdup` — check for duplication
5. `dotnet format` — format code

## Guidelines

- Always build the solution before running `roslynator analyze` — it needs compiled output
- Use `roslynator fix` for auto-fixing, `roslynator analyze` for CI reporting
- Use `--severity-level warning` to skip info/hidden diagnostics in CI
- Use `--ignored-diagnostics` to suppress known false positives
- Use `roslynator find-unused` periodically to catch dead code
- Configure rules in `.editorconfig` rather than via CLI flags for consistency across team
- When the user mentions "lint", "static analysis", or "code quality" in a .NET context, this is the tool to use
