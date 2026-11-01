# Independent review, correction and adaptive retries

Read when beginning Complex final review, correcting review findings, or when a task/package repeatedly fails.

## Independent review (Complex required)

1. Start **only after** work packages are accepted, integration is completed, and global checks have been attempted and recorded. Do not use the implementation agent as its own independent reviewer.
2. Assign a separate subagent a **read-only instruction**: compare actual diff/files with saved design revision, approved/current plan, ACs and constraints. Review public contracts, failure cases, cross-package interactions, risky code paths, and adequacy of tests. Do not edit code, plan or other files; avoid write-producing test commands unless explicitly coordinated.
3. Request actionable findings with severity, affected files/locations, reproducible evidence or rationale, affected ACs and a proposed correction direction. Ask the reviewer to distinguish verified defects from suspicions.
4. Main agent triages findings. Reject with reason if false or out of scope. Fix genuine `critical` and `major` findings, and `minor` findings that represent real defects. Treat nonessential improvement suggestions as optional; never expand scope without permission.
5. For localized fixes, reuse the original responsible subagent if its context remains useful; otherwise assign another safe subagent. Main agent owns integration/cross-package fixes. Run conflicting fixes sequentially in the shared working directory.
6. Re-run affected checks and regression checks. Ask an independent reviewer to recheck meaningful corrections; use focused re-review for localized edits and broader review if several subsystems or contracts changed. A review report does not prove a test was run.

**Note:** "review-only" here is a behavioral requirement of the subagent, not a technical filesystem-enforced permission boundary. If the environment supports enforceable read-only access, use it; otherwise inspect whether the reviewer changed any files and address unexpected mutations.

## Adaptive retry policy

- Diagnose failure from actual logs, affected code, and reproduced behavior; distinguish product defect, environment failure, flaky test and wrong test expectation.
- Each retry must make a **specific change or test a distinct hypothesis**. Never blindly rerun the same failing action without changed conditions or a plausible transient cause.
- After repeated attempts without meaningful progress (normally by the second unsuccessful correction), **pause the current retry loop and reassess root cause/strategy**. This is a trigger for diagnosis, **not a fixed maximum of attempts**.
- Continue when there is a materially new, testable hypothesis with a reasonable chance of progress. If no new evidence or viable action exists, mark the affected work `blocked`, record what failed and what input/action would unblock it, and tell the user. Do not spin indefinitely.
- Do not switch from `strict-tdd` to a looser mode or change approved public architecture to make a test pass without appropriate approval.
- For TDD: failed test must demonstrate the intended missing behavior, not just a broken build or unrelated failure; green requires actual test success; refactor follows with regression checks. If not feasible, record and escalate rather than claiming strict compliance.

## Minimal findings record in plan

```markdown
## Review findings
- R1 [major, open] — Null RPC arguments are mis-encoded (AC3).
  - Owner: WP-02
  - Evidence: <test/result or file:line>
  - Next step: <specific proposed change or investigation>
```

Keep unresolved and significant items, not raw reviewer transcripts. Close findings only after evidence-based checking. Keep the final review outcome and remaining known risks in the plan.
