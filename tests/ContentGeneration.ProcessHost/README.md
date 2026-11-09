# Production generation process checks

This standalone executable exercises the real `ContentBuildCoordinator` and
`ContentGeneration` APIs in separate processes. It is independent of the earlier
filesystem-lock feasibility experiment.

From the repository root:

```sh
dotnet build tests/ContentGeneration.ProcessHost/ContentGeneration.ProcessHost.csproj -m:1 -nr:false
dotnet tests/ContentGeneration.ProcessHost/bin/Debug/net10.0/ContentGeneration.ProcessHost.dll selftest
```

An optional directory after `selftest` selects the filesystem to test. The program
creates and removes a unique subdirectory. Every child has a bounded response and
exit deadline; the process exits with code 1 when an invariant fails.

Checks cover two live owners across replacement, lazy reads after one owner dies,
reclamation after the final owner dies, simultaneous publishers retaining both
results, twelve acquisition/collection races, and process death while an actual
build holds its namespace gate. The last check pauses inside a processor before
publication. It proves gate crash recovery, not interruption of each filesystem
publication phase. Modeled publication-phase recovery belongs to the generation
unit tests; do not describe this executable as exhaustive crash-durability proof.

The Windows/Linux CI matrix runs this executable on its workspace filesystem.
Passing on one filesystem does not establish locking or crash semantics on other
filesystems or protect against uncooperative external mutation.
