# Normal mode

Use when the effective development mode is `normal`. It is standard, verification-driven development, **not permission to omit tests**.

1. Inspect affected behavior, surrounding code and existing tests; identify suitable checks and risks.
2. Implement the smallest coherent change consistent with design and acceptance criteria.
3. Add/update automated tests where behavior needs regression coverage, using existing project conventions. Tests may be written before or after implementation; no Red-first evidence is required.
4. Run relevant unit/integration tests, builds, static checks and/or direct UI/manual checks. Do not substitute a successful compilation for behavioral correctness when meaningful tests are possible.
5. Fix confirmed defects, rerun relevant checks and report what passed, failed or could not be run.

For refactoring, prefer existing characterization tests; add missing regression tests when practical. For non-code changes, verify the actual artifact (e.g., configuration parses, build command succeeds, documentation references remain valid).
