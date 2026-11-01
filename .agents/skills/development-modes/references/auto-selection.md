# Test-first Auto selection

Use only when the requested development mode is `auto`. The aim is test-first for verifiable behavior without forcing TDD on changes where it adds no value. Selection is separate from light/medium/complex workflow complexity.

## Selection rules

| Change | Effective mode | Why |
| --- | --- | --- |
| New feature with observable, automatable behavior | `tdd` | Tests specify behavior before implementation. |
| Reproducible bug with a feasible automated regression test | `tdd` | Capture failure first, then fix. |
| Behavior work with no current tests/framework, but testing is feasible | `tdd` | **Set up test infrastructure first**; absence alone does not cause fallback. |
| Refactor intended to preserve behavior, with existing usable coverage | `normal` | Run characterization/regression tests around the change. |
| Build scripts, infrastructure, configuration, documentation | `normal` | Prefer direct, relevant verification rather than contrived unit tests. |
| UI/visual or external behavior that cannot reasonably be verified automatically in the present environment | `normal` | Use manual/visual/integration verification and record limitations. |
| Research, spike, or experiment without an explicit prototype request | `normal` | Auto does not assume disposable prototype quality. |

Never choose `strict-tdd` or `prototype` automatically. An explicit user choice overrides this table.

## Procedure

1. Determine whether the requested change has observable behavior and a practical automated assertion, taking into account the project's architecture and environment.
2. Choose `tdd` for testable behavior even if it first requires a test project, harness or runner; use [test-infrastructure.md](test-infrastructure.md) to establish it.
3. Otherwise choose `normal`, naming the relevant direct verification method. Do not claim something is untestable solely because current tests are absent.
4. A mixed plan may keep `development_mode: tdd` as its effective default and mark exceptional tasks `normal` individually. Avoid storing repeated mode metadata for all default-mode tasks.
5. If new information changes whether a task is testable, revisit only that task's automatic choice. Main agent records a meaningful plan adjustment; notify the user only if behavior, architecture, scope or agreed constraints must change.

If the user explicitly selected any non-auto mode, **do not** apply these automatic exceptions.
