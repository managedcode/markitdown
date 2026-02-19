---
name: cloc
description: Count lines of code, analyze codebase size and composition, compare code between versions. Use when the user asks about lines of code, codebase size, language breakdown, or code statistics.
argument-hint: "[path or git ref]"
---

## Prerequisites

Before running cloc, check if it is installed:

```
which cloc
```

If not found, install it:
- macOS: `brew install cloc`
- npm: `npm install -g cloc`
- Debian/Ubuntu: `sudo apt install cloc`
- Red Hat/Fedora: `sudo yum install cloc`

## About cloc

cloc counts blank lines, comment lines, and physical lines of source code in 200+ programming languages. It can analyze files, directories, archives (tar, zip, .whl, .ipynb), and git commits/branches.

## Common Usage

**Count a directory (default: current project):**
```
cloc .
```

**Count using git file list (respects .gitignore):**
```
cloc --vcs=git
```

**Count a specific path:**
```
cloc $ARGUMENTS
```

**Count by file (detailed breakdown):**
```
cloc --by-file --vcs=git
```

**Diff between two git refs:**
```
cloc --git --diff <ref1> <ref2>
```

**Exclude directories:**
```
cloc --exclude-dir=node_modules,vendor,.git .
```

## Output Formats

Use these flags for machine-readable output:
- `--json` — JSON
- `--yaml` — YAML
- `--csv` — CSV
- `--md` — Markdown table
- `--xml` — XML

## Useful Flags

| Flag | Purpose |
|------|---------|
| `--by-file` | Per-file breakdown instead of per-language |
| `--vcs=git` | Use git to get file list (respects .gitignore) |
| `--git` | Treat arguments as git targets (commits, branches) |
| `--diff <a> <b>` | Show added/removed/modified lines between two sources |
| `--exclude-dir=<d1,d2>` | Skip directories |
| `--exclude-lang=<l1,l2>` | Skip languages |
| `--include-lang=<l1,l2>` | Only count these languages |
| `--force-lang=<lang>,<ext>` | Map file extension to a language |
| `--processes=N` | Parallel processing (N cores) |
| `--quiet` | Suppress progress output |

## Guidelines

- Prefer `--vcs=git` over bare `.` to avoid counting generated/vendored files
- Use `--json` when the user needs to process the output programmatically
- When comparing versions, use `--git --diff <ref1> <ref2>`
- For large repos, consider `--processes=N` to speed things up
