# Prototype mode

Use only when **explicitly selected**. Goal: test a defined hypothesis with the smallest useful implementation, rather than deliver production-ready breadth.

1. State the hypothesis and the observable result that will support or refute it. If a saved design/plan already defines this, reuse it.
2. Build the minimum spike/prototype needed; avoid speculative abstractions, premature optimization and broad refactoring.
3. Choose lightweight, direct verification of the hypothesis: a focused smoke test, demo, benchmark, integration trial or observable manual check as appropriate.
4. Preserve project safety constraints: do not expose secrets, corrupt user data, or make destructive changes without authorization. Don't bypass mandatory project/security checks merely because this is a prototype.
5. Identify shortcuts and what remains before production readiness (tests, error handling, cleanup, performance validation, maintainability).
6. Report whether the hypothesis was confirmed, contradicted or remains untested, with actual evidence.

Do not present a prototype as production-complete. `auto` never selects this mode. Do not automatically deploy or integrate disposable prototype code into production workflows unless the user explicitly requests that scope.
