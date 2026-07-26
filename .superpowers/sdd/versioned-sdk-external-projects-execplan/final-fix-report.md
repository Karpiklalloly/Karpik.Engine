# Final review remediation report (R1-R4)

## Scope and implementation commit

Implementation commit: `00be65c` (`fix: harden versioned SDK review findings`).

Remediated the final-review findings only:

- converted the five symlink capability tests from silent `return` paths to explicit xUnit v3 skips;
- preserved unprivileged filesystem failures as failures by skipping `UnauthorizedAccessException` and `IOException` only when Windows reports `ERROR_PRIVILEGE_NOT_HELD` (`0x80070522`);
- replaced root-destroying `TrimEnd` normalization with `Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))`;
- added the focused filesystem-root normalization regression test and renamed the inaccurate linked-directory test;
- removed only the stale `MyGame` token from the retained `SA001` analyzer setting.

No `graphify-out/` or `SUMMARY.md` changes were staged or committed.

## Root cause

The five capability tests caught `PlatformNotSupportedException`, `UnauthorizedAccessException`, and `IOException` together and then returned from `[Fact]` methods. xUnit reported those returns as passes, including unrelated I/O and access failures. The existing newer tests already established the required narrow policy: use `SkipException.ForSkip` for platform support and only the exact Windows privilege HRESULT; propagate all other errors.

`HasReparsePointAncestor` used `TrimEnd` after `GetFullPath`. At a filesystem root this changes `C:\` to `C:` (or `/` to an empty string), so subsequent ancestor traversal can be drive-relative or invalid instead of rooted. `Path.TrimEndingDirectorySeparator` preserves filesystem roots.

`OpenAsync_RejectsRuntimeBundleBelowExistingLinkedDirectoryEscapingActiveGameRoot` did not prove an active-root escape: its target directory was still below the active root. Its renamed test accurately describes the existing fixture.

`Tools/StaticAnalyzer/.editorconfig` retained `MyGame` as an unused prohibited-directory token while `SA001` itself remains configured. The minimal safe cleanup removes that one stale token rather than deleting the setting.

## RED/GREEN evidence

1. Added `RuntimeBundleRootNormalization_PreservesFilesystemRoot` before the production normalization helper.
2. RED command:

   ```powershell
   dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~RuntimeBundleRootNormalization_PreservesFilesystemRoot"
   ```

   Result: 1 failed, 0 passed, 0 skipped. It failed at `Assert.NotNull()` because `NormalizeBundleRoot` did not exist.
3. Implemented `NormalizeBundleRoot` with `Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))` and called it from `HasReparsePointAncestor`.
4. GREEN command (with `--no-restore`): same filter above.

   Result: 1 passed, 0 failed, 0 skipped.

The initial attempt to prove the bug through a real link was explicitly skipped because this host lacks the Windows symbolic-link privilege. The final regression test intentionally has no link-capability dependency and verifies root preservation directly.

## Required affected-test verification

All commands used one MSBuild node and disabled node reuse.

| Command | Result |
| --- | --- |
| `dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false --no-restore` | 39 passed, 0 failed, 3 skipped |
| `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ProjectOpenServiceTests"` | 15 passed, 0 failed, 5 skipped |
| `dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false --no-restore` | 43 passed, 0 failed, 0 skipped |

All eight skips are explicit capability skips caused by unavailable symbolic-link privilege: three SDK task cases and five ProjectOpenService cases. They are not silent passes.

The builds emitted pre-existing compiler warnings and offline `NU1900` vulnerability-metadata warnings; no test failed. The Editor test command required normal local access for Avalonia's build telemetry log, which the sandbox initially denied; the verified runs used the approved environment.

## Self-review

- Searched the two affected test files for the prior broad `catch (...) { return; }` pattern: no matches.
- Confirmed each changed capability path catches `PlatformNotSupportedException` explicitly and gates `UnauthorizedAccessException`/`IOException` on `OperatingSystem.IsWindows()` plus `0x80070522`.
- Confirmed the root normalization uses the exact required API and its focused test passes.
- Ran `git diff --check` before staging and `git diff --cached --check` before the implementation commit: no whitespace errors.
- Staged only the four implementation-owned files; pre-existing `graphify-out/` and `SUMMARY.md` worktree changes remained unstaged.
