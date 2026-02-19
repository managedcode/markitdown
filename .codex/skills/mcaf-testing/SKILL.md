---
name: mcaf-testing
description: "Add or update automated tests for a change (bugfix, feature, refactor) using the repository’s testing rules in AGENTS.md. Use TDD (test fails → implement → pass) where applicable; derive scenarios from docs/Features/* and ADR invariants; prefer stable integration/API/UI tests, run build before tests, collect coverage, and verify meaningful assertions for happy/negative/edge cases."
compatibility: "Requires the repository’s build/test tooling; uses commands from AGENTS.md."
---

# MCAF: Testing

## Outputs

- New/updated automated tests that encode **documented behaviour** (happy path + negative + edge), with integration/API/UI preferred
- For new behaviour and bugfixes: tests drive the change (TDD: reproduce/specify → test fails → implement → test passes)
- Updated verification sections in relevant docs (`docs/Features/*`, `docs/ADR/*`) when needed (tests + commands must match reality)
- Evidence of verification: commands run (`build`/`test`/`coverage`/`analyze`) + result + the report/artifact path written by the tool (when applicable)

## Workflow

1. Read `AGENTS.md`:
   - commands: `build`, `test`, `format`, `analyze`, and the repo’s coverage path (either a dedicated `coverage` command or a `test` command that generates coverage)
   - testing rules (levels, mocks policy, suites to run, containers, etc.)
2. Start from the docs that define behaviour (no guessing):
   - `docs/Features/*` for user/system flows and business rules
   - `docs/ADR/*` for architectural decisions and invariants that must remain true
   - if the docs are missing/contradict, fix the docs first (or write a minimal spec + test plan in the task/PR)
   - follow `AGENTS.md` scoping rules (Architecture map → relevant docs → relevant module code; avoid repo-wide scanning)
3. Follow `AGENTS.md` verification timing (optimize time + tokens):
   - run tests/coverage only when you have a reason (changed code/tests, bug reproduction, baseline confirmation)
   - start with the smallest scope (new/changed tests), then expand to required suites
4. For "fix failing tests" tasks, triage in batches before coding:
   - run the target full suite first (not one test at a time)
   - collect the complete failing-test list
   - write/refresh a root `*.plan.md` checklist with each failing test and status
   - fix from that checklist in required order (integration -> API -> UI)
   - for each layer, repeat this loop until full-suite `x0`: full suite -> plan update -> fixes -> targeted retests -> full suite again
   - after all three layers are `x0`, run one final full regression
5. Define the scenarios you must prove (map them back to docs):
   - **positive** (happy path)
   - **negative** (validation/forbidden/unauthorized/error paths)
   - **edge** (limits, concurrency, retries/idempotency, time-sensitive behaviour)
   - for ADRs: test the **invariants** and the “must not happen” behaviours the decision relies on
6. Choose the highest meaningful test level:
   - prefer integration/API/UI when the behaviour crosses boundaries
   - use unit tests only when logic is isolated and higher-level coverage is impractical
7. Implement via a TDD loop (per scenario):
   - write the test first and make sure it fails for the **right reason**
   - implement the minimum change to make it pass
   - refactor safely (keep tests green)
8. Write tests that assert outcomes (not “it runs”):
   - assert returned values/responses
   - assert DB state / emitted events / observable side effects
   - include negative and edge cases when relevant
9. Keep tests stable (treat flakiness as a bug):
   - deterministic data/fixtures, no hidden dependencies
   - avoid `sleep`-based timing; prefer “wait until condition”/polling with a timeout
   - keep test setup/teardown reliable (reset state between tests)
10. Coverage (follow `AGENTS.md`, optimize time/tokens):
   - run coverage only if it’s part of the repo’s required verification path or if you need it to find gaps
   - run coverage once per change (it is heavier than tests)
   - capture where the report/artifacts were written (path, summary) if generated
11. If the repo has UI:
   - run UI/E2E tests
   - inspect screenshots/videos/traces produced by the runner for failures and obvious UI regressions
12. Run verification in layers (as required by `AGENTS.md`):
   - new/changed tests first
   - then the related suite
   - then broader regressions if required
   - run `analyze` if required
13. Keep docs and skills consistent:
   - ensure `docs/Features/*` and `docs/ADR/*` verification sections point to the real tests and real commands
   - if you change test/coverage commands or rules, update `AGENTS.md` and this skill in the same PR

## Guardrails

- All test discipline and prohibitions come from `AGENTS.md`. Do not contradict it in this skill.
