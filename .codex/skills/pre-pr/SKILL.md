---
name: pre-pr
description: Run mandatory pre-PR quality checks for .NET projects before creating a pull request. Use before creating any PR, or when the user says "prepare for PR", "pre-PR checks", or "quality gate".
disable-model-invocation: true
argument-hint: "[solution or project path]"
---

## Prerequisites

Check that required tools are available:

```
which roslynator
which quickdup
```

If `roslynator` is not found:
```
dotnet tool install -g roslynator.dotnet.cli
```

If `quickdup` is not found:
- macOS/Linux: `curl -sSL https://raw.githubusercontent.com/asynkron/Asynkron.QuickDup/main/install.sh | bash`
- From source: `go install github.com/asynkron/Asynkron.QuickDup/cmd/quickdup@latest`

## About

This is a mandatory quality gate to run before creating any pull request in a .NET project. It ensures code is analyzed, compiles cleanly, tests pass, duplication is checked, and code is formatted — in that order.

Do NOT skip any steps. Do NOT create a PR if any step fails. Iterate until all checks pass.

## Determine Paths

If `$ARGUMENTS` is provided, use it as the project/solution path. Otherwise, look for a `.sln` or `.csproj` in the current directory.

Also determine the source directory for quickdup scanning (typically `src/` or the project root).

## Step 1: Roslynator Fix

Run static analysis and auto-fix code issues:

```bash
roslynator fix $PROJECT_PATH
```

Then verify compilation:

```bash
dotnet build $PROJECT_PATH
```

**If build fails:** Fix the issues and rerun roslynator before proceeding.

## Step 2: Run Tests

```bash
dotnet test $PROJECT_PATH
```

**Handle failures:**
- **Flaky tests:** Rerun to confirm
- **Broken tests:** Fix before proceeding
- **All tests MUST pass** before moving to step 3

## Step 3: Check for Code Duplication

```bash
quickdup -path $SOURCE_DIR -ext .cs -select 0..20 -min 2 -exclude ".g.,.generated."
```

**If new duplications are found:**
1. Refactor to eliminate duplications (see `/quickdup` for refactoring patterns)
2. Go back to Step 1 (roslynator fix)
3. Repeat until no new duplications

## Step 4: Format Code

```bash
dotnet format $PROJECT_PATH
```

## Step 5: Final Verification

Confirm all checks passed:
- [ ] Roslynator fix applied
- [ ] Code compiles cleanly
- [ ] All tests pass
- [ ] No new code duplications
- [ ] Code formatted

## Step 6: Create PR

Only after ALL checks pass, create the pull request.

## Important

- Do NOT skip any steps
- Do NOT create PR if any step fails
- Steps are ordered intentionally — roslynator before build, build before test, duplication check may loop back to step 1
- Iterate until all checks pass
