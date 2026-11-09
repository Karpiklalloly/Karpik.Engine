# Strict TDD mode

Use only when **explicitly selected** by the user/workflow. Strict TDD is mandatory for every applicable change in observable, automatable behavior. It may not be silently weakened by a plan, agent or missing infrastructure.

## Required Red → Green → Refactor sequence

1. Choose one small observable behavior and define its expected outcome from the design/ACs.
2. **Red:** write an automated test first; execute it and capture the expected failing assertion/behavior. Verify the runner itself works. A broken environment or unrelated compilation error is **not** sufficient evidence of Red.
3. **Green:** write the minimal production code needed to pass that test; execute the test again and confirm it passes.
4. **Refactor:** improve structure while preserving behavior; rerun the relevant passing tests. If no code cleanup is needed, record that the refactor check was performed and no change was required.
5. Repeat for further behavior; perform final targeted and integration/global verification.

Before any behavior implementation, create missing or repair broken test infrastructure using [test-infrastructure.md](test-infrastructure.md). Establish that an example or smoke test runs successfully, then begin Red/Green/Refactor cycles.

## Scope and blocked cases

- Infrastructure wiring, build configuration, documentation or similar **non-behavior** work must receive direct verification; do not invent meaningless unit tests solely to simulate Red. Strict TDD applies to behavior that can be expressed as automated tests.
- If a behavior cannot be tested in the available environment, or its testing requires an architectural change, pause the affected behavior work, describe the obstacle and seek an explicit decision/approval. Do not silently substitute `normal`, a manual check, or fabricated Red evidence.
- If a test passes before the intended implementation, it does not demonstrate Red. Improve the test to detect the missing behavior where appropriate, or report that the behavior already exists.
- A new test written after an implementation is useful coverage but cannot retroactively demonstrate test-first compliance. Report the actual sequence.

## Evidence

Retain minimal factual evidence for each meaningful behavior increment: test identity, observed Red reason, Green result, and post-refactor result. Avoid verbose command transcripts or separate logs unless the environment already requires them. Complex subagents report evidence to the main agent, which verifies acceptance.
