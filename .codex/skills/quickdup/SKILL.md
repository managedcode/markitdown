---
name: quickdup
description: Find and reduce code duplication, clean up redundant code, detect code clones, reduce codebase size, DRY violations, copy-paste detection. Use when the user asks about duplicate code, code cleanup, reducing code size, DRY principles, or finding copy-pasted code.
argument-hint: "[path or extension]"
---

## Prerequisites

Before running quickdup, check if it is installed:

```
which quickdup
```

If not found, install it:
- macOS/Linux: `curl -sSL https://raw.githubusercontent.com/asynkron/Asynkron.QuickDup/main/install.sh | bash`
- Windows: `iwr -useb https://raw.githubusercontent.com/asynkron/Asynkron.QuickDup/main/install.ps1 | iex`
- From source: `go install github.com/asynkron/Asynkron.QuickDup/cmd/quickdup@latest`

## About QuickDup

QuickDup is a fast structural code clone detector (~100k lines in 500ms). It uses indent-delta fingerprinting to find duplicate code patterns. It is designed as a candidate generator — it optimizes for speed and recall, surfacing candidates fast for AI-assisted review.

Results are written to `.quickdup/results.json` and cached in `.quickdup/cache.gob` for fast incremental re-runs.

## Workflow

1. **Run quickdup** to find duplication candidates
2. **Review the results** with `-select` to inspect specific patterns
3. **Classify each pattern** — determine which type of duplication it is (see below)
4. **Refactor using the right pattern** — not all duplication should be removed the same way
5. **Re-run** to verify duplication is reduced

## Determine File Extension

Before running, determine the primary language/extension of the project. Look at the files in the target path and use the appropriate `-ext` flag (e.g. `.go`, `.ts`, `.cs`, `.py`, `.java`, `.rs`).

## Common Usage

**Scan current project:**
```
quickdup -path . -ext .go
```

**Scan with specific extension (adapt to project language):**
```
quickdup -path $ARGUMENTS -ext .ts
```

**Show top 20 patterns:**
```
quickdup -path . -ext .go -top 20
```

**Inspect specific patterns in detail:**
```
quickdup -path . -ext .go -select 0..5
```

**Strict similarity (fewer false positives):**
```
quickdup -path . -ext .go -min-similarity 0.9
```

**Require more occurrences:**
```
quickdup -path . -ext .go -min 5
```

**Exclude generated files:**
```
quickdup -path . -ext .go -exclude "*.pb.go,*_gen.go,*_test.go"
```

**Compare duplication between branches:**
```
quickdup -path . -ext .go -compare origin/main..HEAD
```

## Key Flags

| Flag | Default | Purpose |
|------|---------|---------|
| `-path` | `.` | Directory to scan |
| `-ext` | `.go` | File extension to match |
| `-min` | `2` | Minimum occurrences to report |
| `-min-size` | `3` | Minimum pattern size (lines) |
| `-max-size` | `0` | Max pattern size (0 = unlimited) |
| `-min-score` | `5` | Minimum score threshold |
| `-min-similarity` | `0.75` | Token similarity threshold (0.0-1.0) |
| `-top` | `10` | Show top N patterns |
| `-select` | — | Detailed view (e.g. `0..5`) |
| `-exclude` | — | Exclude file globs (comma-separated) |
| `-no-cache` | `false` | Force full re-parse |
| `-strategy` | `normalized-indent` | Detection strategy |
| `-compare` | — | Compare between commits (`base..head`) |

## Detection Strategies

- **normalized-indent** (default) — indent deltas + first word per line
- **word-indent** — raw indentation + first word
- **word-only** — ignores indentation, first words only
- **inlineable** — detects small patterns suitable for inline extraction

## Suppressing Known Patterns

If a pattern is intentional duplication, suppress it by adding its hash to `.quickdup/ignore.json`:

```json
{
  "description": "Patterns to ignore",
  "ignored": ["56c2f5f9b27ed5a0"]
}
```

---

## How to Refactor Duplicated Code

QuickDup finds duplication candidates. The next step is knowing *how* to fix them. Not all duplication is the same — each type has a different refactoring strategy.

### Type 1: Structural Duplication with Contextual Variation

Two blocks share the same control flow and structure, but differ in injected behavior (which function is called, which parameters are passed, whether some context like an index exists).

**How to recognize it:**
- Same loops, same branching, same method calls in the same order
- Only the behavior inside the structure differs

**How to refactor:**

1. **Ignore the differences** — mentally replace the differing lines with placeholders. If the code still "reads the same", you have structural duplication.
2. **Identify the invariant structure** — same loops, branching, method calls in the same order. That is the structure to extract.
3. **Identify the varying behavior** — what methods differ? What parameters differ? What context exists in one version but not the other? These become delegates, lambdas, or strategy objects.
4. **Extract, do not merge** — do NOT write `if (variant) { ... } else { ... }`. Instead, extract the shared structure and pass behavior in.
5. **Preserve meaning at call sites** — the call site should still clearly express intent.

Good:
```
IterateAndResolve(array, i => CreateResolve(i), ...)
```

Bad:
```
IterateAndResolve(array, withIndex: true)
```

**Rule of thumb:** If two methods differ only in what they do inside a shared structure, extract the structure. If they differ in structure, keep them separate.

### Type 2: Structural Switch Duplication

Repeated switch/pattern match blocks on the same domain structures. Common in AST walkers, serializers, interpreters, and compilers.

**How to recognize it:**
- Repeated `switch` or pattern matching blocks
- Short, trivial case bodies
- Each case extracts or forwards structure
- No real algorithm, just classification

**When to leave it alone:**
- Each case is one or two lines
- The code is stable and unlikely to drift
- The abstraction would just wrap a switch
- The switch *documents the domain shape* — removing it hides meaning

**When to refactor:**
- Same switch appears 3+ times
- Case list starts diverging between copies
- Case logic grows beyond trivial access

**How to refactor:**
- Extract the *smallest possible* helper
- Return data, do not hide control flow
- Use pattern matching, not flags

Good: `Statement? TryUnwrapBody(Statement s)`
Bad: `HandleStatement(s, mode)`

**Rule of thumb:** If the switch *describes structure*, duplication is documentation. If the switch *implements behavior*, consider extraction.

### Type 3: Parameter Bundle Duplication

The same set of context values passed together in multiple places — same parameters, same order, same meaning.

**How to recognize it:**
- Same group of 4+ parameters passed together repeatedly
- Usually forwarded to constructors or methods
- Reads like a "context snapshot"
- Adding a new parameter would require editing many call sites

**How to refactor:**

Extract a value object:
```
new ExecutionContext(thisValue, realmState, isStrict, homeObject, privateScope)
```

Then pass the single object instead of 5+ individual arguments.

**When to leave it:** Appears only once, is highly localized, or has extremely short lifetime.

**Rule of thumb:** If the same parameter list appears 2-3+ times, it wants to become a named type.

### Type 4: Argument Unpacking Duplication

Duplicated defensive parsing of callback arguments — same local variables, same guards, same positional decoding.

**How to recognize it:**
- Same argument index checks (`args.Count >= 1`, `args[0]`)
- Same type unwrapping logic
- Same local variable names
- Appears inside lambdas, which amplifies the noise

**How to refactor:**

Extract a small helper whose only job is to unpack the arguments:
```
UnwrapResolveReject(args, out var resolve, out var reject);
```

This is purely mechanical code — the intent is obscured by boilerplate, and any bug fix would need to be applied to every copy.

**Rule of thumb:** If you copy the same argument decoding logic twice, extract it. If the extraction reads like English, you picked the right abstraction.

---

## Guidelines

- Always determine the correct `-ext` for the project before running
- Start with defaults, then tighten `-min-similarity` if too many false positives
- Use `-select 0..5` to inspect the top candidates before refactoring
- After identifying duplicates, classify them by type before refactoring
- Not all duplication should be removed — structural switch duplication is often intentional documentation
- After refactoring, re-run to confirm duplication is reduced
- For large projects, quickdup caches results — subsequent runs are near-instant
- When the user wants to clean up or reduce code size, run quickdup first to identify targets, then refactor the highest-scoring patterns using the appropriate strategy for each type
