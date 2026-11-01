# TDD mode

Use when the effective development mode is `tdd`, whether manually selected or chosen by Test-first Auto. Follow test-first development for automatable behavior **by default**.

## Loop

1. Identify the next small observable behavior from the acceptance criteria.
2. Write a focused automated test first.
3. Run it and establish that it fails for the expected missing behavior/bug (Red), not merely due to a broken test runner or unrelated environment failure.
4. Implement enough to pass (Green); run the focused test.
5. Refactor as useful without changing behavior; rerun relevant tests.
6. Repeat for the next behavior and finish with integration/global checks.

If the test framework, runner, mocks/fixtures or test project do not exist or are unusable, first follow [test-infrastructure.md](test-infrastructure.md). Do not silently switch to `normal` due to setup cost.

## Exceptions

- Conventional `tdd` permits **justified** departures from test-first for steps not reasonably expressible as automated behavior tests (e.g., documentation, project wiring, some visual adjustments). Explain the reason and use an appropriate direct verification method.
- Manual `tdd`: do not change the requested mode; treat such non-behavior steps as scoped exceptions, not a global downgrade. For disputed testable behavior, ask the user rather than silently bypassing test-first.
- Auto-selected `tdd`: a specific genuinely untestable task may instead be classified `normal` under [auto-selection.md](auto-selection.md). Missing infrastructure alone is **not** sufficient.
- Do not fabricate a Red phase if an existing test already passes or implementation predates test creation. State actual sequence and add meaningful regression coverage.

Record tests/commands and results concisely, especially any test-first exception that affects confidence.
