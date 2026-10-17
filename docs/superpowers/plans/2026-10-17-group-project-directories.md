# Group Project Directories Implementation Plan

**Goal:** Move engine/tool projects into `src/` and tests into `tests/`, preserving `_scripts` and runtime behavior.

**Architecture:** Mechanical repository-layout change approved in chat on 2026-10-17. Preserve assembly names, namespaces, module layout, submodules and external game layouts. Shared MSBuild files stay at the root.

**Tech Stack:** .NET 10, MSBuild, C#, PowerShell, SLNX.

**Spec:** The approved chat design is recorded in [ExecPlan](../../../plans/group-project-directories-execplan.md), the authoritative milestone/status overview.

## Global Constraints

Keep `_scripts`, `Modules`, `templates`, `Generated`, `first-parties`, `third-parties`, docs and shared root files at their existing paths. No runtime algorithm or API changes. Use single-node MSBuild with node reuse disabled. Do not modify submodule checkouts or historical plans.

## Review Focus

Root discovery from moved test outputs; module references into moved projects; generated relative paths imported by publication projects; scripts building SDK payloads and templates; external game fixtures whose paths are intentionally independent of this checkout.

## Task 1: Relocate projects and update paths

- [x] Enumerate tracked source and test projects, preserving tests inside Modules and submodules.
- [x] Move 38 directories and update references, imports, solution paths and filesystem literals.
- [x] Update root discovery and build/test fixtures without changing external project layouts.
- [x] Update CI, `_scripts` and current user-facing documentation.
- [x] Validate literal project references and solution paths; regenerate catalog and inspect diff. Extension submodule's old core reference is redirected in root targets. Independent review found no actionable regressions.

## Task 2: Verify the resulting checkout

- [x] Run Configurator generation and validation from `src/Configurator`; also run `_scripts/Update-Modules.ps1`: both operations passed.
- [x] Build the complete solution: `dotnet build KarpikEngine.slnx -c Release -m:1 -nr:false -p:NuGetAudit=false` passed, 0 errors and 81 warnings (6:33). Local audit disabled because network vulnerability lookup is unavailable; repository/CI audit configuration unchanged.
- [x] Final non-integration solution tests passed: 955 passed / 9 skipped / 0 failed, 17 assemblies; exit 0. Two erroneous SDK identifier replacements were restored and ProjectModel retested (31/31); the skipped integration fixture's SDK identifier was also restored and its assembly rebuilt before final execution.
- [x] Pack SDK and templates using `_scripts/New-KarpikSdkPackages.ps1 -SdkVersion 0.7.0-layout -Output artifacts/layout-packages`; package payload assertions passed (SDK 66 entries, templates 142).
- [x] Imported-reference SDK smoke passed all three cases. Solution-scope smoke rejected both invalid orders. Its preexisting positive Runtime fixture lacks KarpikModuleSelection and fails KARPIK011 even with a package matching the installed SDK version; this unrelated baseline limitation is retained, not counted as a passing check.
- [x] Review remaining old-path matches, avoiding changes to historical records; seven PowerShell syntax checks passed and dependency submodule revisions remain unchanged.

## Task 3: Deliver

- [x] Update both plans with executed commands and results.
- [x] Review diff and whitespace, commit only task changes (`87ae931`).
- [x] Push `codex/group-project-directories` and create [PR #102](https://github.com/Karpiklalloly/Karpik.Engine/pull/102) against main, labeled enhancement. Initial sandbox publication refusal was resolved by verifying origin is the user's PUBLIC repository and the authenticated account has ADMIN permission; the permitted retry succeeded. Merge remains separate.
