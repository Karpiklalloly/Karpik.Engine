# Mandatory test infrastructure for TDD

Read when `tdd` or `strict-tdd` is selected and tests cannot yet be executed reliably. **Setup precedes implementation of the behavior under test, regardless of setup effort.** This applies to automatically selected TDD too.

## Setup

1. Inspect existing project targets, runtime, build tooling, conventions, existing test projects and CI commands. Prefer extending a working test harness over introducing a second framework.
2. Determine the minimum infrastructure necessary to run a meaningful automated test for the selected behavior: test project, runner, dependencies, fixtures/fakes, test doubles, launch configuration or isolated host where required.
3. Create/repair that infrastructure with minimal architectural impact and without destructive changes. Align versions and dependencies with the repository, and avoid global tooling modifications without permission.
4. Execute a small known-passing smoke test to prove the harness can discover, execute and report tests. Only then begin a new failing behavior test (Red). A failing setup/restore/build is an infrastructure problem, **not** the TDD Red stage.
5. Include setup in the task/plan when needed. Main agent coordinates shared solution/test project edits in Complex execution; parallel subagents must not edit shared build/CI files without coordinated ownership.
6. Run the relevant tests again after integration, keeping shared test/build output operations serialized when necessary.

## Escalation

- Do **not** fall back to `normal` just because setup consumes time or tokens.
- If an added test project or dependency materially changes previously agreed architecture, public contracts, scope, or safety constraints, pause the affected work and use the workflow's change/approval path.
- If installation, tooling, network access, licensing or permissions block setup, record the concrete error and action required; leave the relevant task `blocked`, not completed. Ask only for necessary access/decision and do not claim TDD evidence.
- For fundamentally non-automatable behavior, distinguish that from a *missing harness*: `tdd` permits justified non-behavior-step exceptions; `strict-tdd` requires explicit resolution before proceeding with untestable behavior.

## Evidence

Record which test runner/project was created or fixed, the smoke-test command and result, and the subsequent Red/Green/Refactor evidence. Keep it compact in the existing plan or task report rather than a separate persistent log.
