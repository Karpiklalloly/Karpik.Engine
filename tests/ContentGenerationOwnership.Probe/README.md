# Content generation ownership feasibility probe

This executable is the WP-01 experiment for
`plans/content-reload-notifications-execplan.md`, not a production storage API.
It has no dependency on engine runtime or frame-loop code.

From the repository root:

```sh
dotnet build tests/ContentGenerationOwnership.Probe/ContentGenerationOwnership.Probe.csproj -m:1 -nr:false
dotnet tests/ContentGenerationOwnership.Probe/bin/Debug/net10.0/ContentGenerationOwnership.Probe.dll selftest
```

An optional existing workspace directory selects the filesystem under test:

```sh
dotnet tests/ContentGenerationOwnership.Probe/bin/Debug/net10.0/ContentGenerationOwnership.Probe.dll selftest /home/artem/git/Karpik.Engine
```

The executable creates a unique subdirectory, launches actual child processes,
fails with exit code 1 on any violated invariant, and removes its own directory.
The same commands work on Windows with a Windows workspace path. A passing Linux
run is not Windows evidence. Each run prints its platform, runtime and test root.
No test result proves semantics on other filesystems or runtime versions.

## Protocol under experiment

Every cooperating acquirer, publisher and collector first exclusively opens the
same permanent `namespace.gate` using `FileShare.None`. That file must never be
deleted, renamed or replaced, including after a crash. Otherwise waiters could
synchronize against different filesystem objects. Creation uses `OpenOrCreate`,
so simultaneous first startup does not elect two independent gates.

Under that gate an acquirer checks that the immutable generation exists and
creates a uniquely named claim file with an open `FileShare.Read` handle. It
retains the handle for the complete use lifetime, including lazy and in-flight
reads. Disposal closes the handle; it leaves a stale claim file. Ownership is an
OS-held open handle, not a PID or timestamp. Multiple independent owners use
independent claim files, including owners in the same process.

A collector holds the same gate from claim examination through deletion. Opening
a claim with `FileShare.None` succeeds only after that owner closed its handle or
died. Any uncertain entry, I/O error or denied access retains the generation.
After proving all claims stale, collection removes the generation. An acquirer
waiting behind collection observes the missing directory and rejects it; it
cannot acquire a generation in the check/delete window.

The probe validates live owners in two processes, death of one and then all
owners, independent same-process owners and gates, gate-owner death, a paused
collector versus an acquiring process, unknown claim entries, retry, 40
acquisition/collection races, and simultaneous first gate creation.

## Assumptions and production requirements

- Only cooperating participants modify a trusted private namespace. On Unix these
  locks are advisory: this is not protection against arbitrary processes,
  malicious symlink replacement, or manual deletion.
- Every participant uses the permanent gate before changing generation or claim
  membership. Claim release only closes the owner handle.
- Generation bytes are immutable while owned. The probe's `Publish` helper is
  fixture setup after prior owners are gone, not an implementation of atomic
  manifest publication or crash durability.
- Filesystem sharing/locking semantics must pass on the actual supported local
  filesystem. Network filesystems and untested platforms remain unsupported or
  fail closed; path existence alone does not establish a safe lock protocol.
- The 15-second retry bound is an experiment watchdog. A production cold-path
  API needs explicit cancellation/timeout results and must never retry in a
  frame/tick path.
- A production collector must prune proven-stale claim files even when another
  owner keeps a generation alive. This small fixture retains them until whole
  generation deletion. Production ownership should be aggregated per captured
  store/generation lifetime, not one claim per read; closing one store must not
  release ownership still needed by unchanged slots or in-flight loads.
- A gate timeout, open failure, unknown schema or unsupported filesystem must
  retain data and surface an observable diagnostic. A generation that cannot be
  safely examined must never be treated as unused.

## Local evidence

Record platform/filesystem run results in the main ExecPlan. CI should run the
same executable separately on Windows and Linux before the WP-01 acceptance
gate is considered complete.
