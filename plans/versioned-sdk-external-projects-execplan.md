# Deliver a versioned SDK and external game-project workflow

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

After this plan is complete, KarpikEngine source, installed engine versions, and games are separate ownership units. A developer can create a game in any directory, keep only standard `.slnx`, `global.json`, and SDK-style `.csproj` files in that game, and run ordinary `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` commands without opening the editor. Every project in the game solution uses `Karpik.Engine.Sdk`, declares its project kind and side, and is checked against the same module and Client/Server/Shared rules in CLI, Rider, CI, and the editor.

The Karpik launcher resolves the exact SDK version pinned by the game, starts the compatible versioned editor, and keeps a recent-project list. An editor owns one active project, but that project may run one server and multiple clients. Installed engine payloads and source-built development payloads have the same layout; games never commit relative references to the engine source checkout.

The durable decision is recorded in `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`. The delivery overview is mirrored in `docs/04_Roadmap/kanban-versioned-sdk-external-projects.md`.

## Progress

- [x] (2026-07-15) Architecture agreed and accepted ADR committed as `b68f055`.
- [x] (2026-07-15) Initial ExecPlan and kanban board created.
- [x] (2026-07-15) Milestone 1: reusable game-solution model and validator complete (`5765d51`, `cdc613f`; review clean; ProjectModel 30/30, Configurator 9/9, module validation passed).
- [x] (2026-07-15) Milestone 2: `Karpik.Engine.Sdk` packs and resolves through standard MSBuild SDK resolution; solution-scope and static-graph consistency gates pass external smokes.
- [x] (2026-07-15) Milestone 3: transactional engine payload packager and resolver complete.
- [x] (2026-07-15) Milestone 3 implementation and automated validation complete: Tooling 32/32, Packager 12/12, the relevant Release project build and Configurator validation passed, and the synthetic repository-mode build proved identical hash/file count plus installation reuse across two clean builds while packing owned SDK task assemblies.
- [x] (2026-07-15) Milestone 3 final acceptance complete: two full-repository runs published and then reused `0.6.0-dev-5270fb65f53bc14fd7cba44424975bab7fb1ede51ee17ca574a6fd09532bb185`; an independent framed SHA-256 audit reproduced the manifest hash across 526 hashed files, verified the strict six-field manifest, 22 isolated module roots with primary assemblies, and no staging/replacement residue.
- [x] (2026-07-15) Milestone 4 complete: the `karpik-game` template materializes outside the repository and its restore/build/test/publish workflow plus `KARPIK001`/`KARPIK005` invalid cases pass 4/4 in the opt-in integration suite.
- [x] (2026-07-15) Milestone 5 complete: Runtime Client/Server builds publish transactional side-pure game bundles, and a versioned engine-owned runner starts an external bundle through an explicit validated launch boundary.
- [x] (2026-07-15) Milestone 5 review hardening complete: canonical bounded bundle proof, post-build transitive Shared content, owned runner build artifacts, exact ready cleanup, real empty-state restart hot reload, and standalone shadow cleanup pass twice through the external workflow.
- [x] (2026-07-15) Milestone 5 re-review hardening complete: IPC handlers are registered before send and removed on every exit, reload ownership is atomic, force-killed workers must confirm exit, publication validates ancestors before mutation and permits proven bundle replacement after assembly rename, the evaluated bundle-path property is inspectable before target execution, bundle identities are case-insensitive, and failed pre-load shadow copies are removed.
- [x] (2026-07-15) Milestone 5 lifecycle serialization complete: one transition owner coordinates start, worker/public reload, stop, and disposal; counted stop/dispose intent prevents replacement after teardown begins, and per-worker deferred exit publication suppresses only a proven planned reload exit.
- [x] (2026-07-16) Milestone 6A foundation complete: raw `.slnx` validation precedes bounded out-of-process MSBuild evaluation, candidate contexts carry exact evaluated bundle/runner paths and generation identities, and a fail-closed coordinator serializes commands and enforces the complete teardown-before-open order.
- [x] (2026-07-17) Milestone 6A review hardening complete: shutdown uses the same retry-safe ordered teardown as switching; Windows retains no-follow/no-write/no-delete handles for every raw/evaluated metadata input through confirmed child-process exit; non-Windows evaluates a bounded owned metadata-only mirror with canonical path remapping; unconfirmed MSBuild termination transfers process, result, and input-lease ownership to a capacity-bounded observable reaper; and MSBuild JSON cleanup/read remains bounded and link-safe (Editor 62/62 including outside-sandbox race/link checks, ProjectModel 31/31).
- [x] (2026-07-18) Milestone 6B remediation complete: the Avalonia shell, startup arguments, open/build/publish/session commands, workspace persistence, and shutdown now compose through `ProjectSwitchCoordinator` and project-owned lifetimes; callbacks and snapshots are generation-gated, and dotnet children are bounded and confirm exit before disposal.
- [x] (2026-07-18) Milestone 6 complete: Editor tests pass 82/82 with one opt-in smoke skipped by default, the editor project builds, and the real two-game external switch smoke passes 1/1 after starting server+client, confirming old PIDs exit, selecting the second game's distinct bundle paths, and confirming final shutdown.
- [x] (2026-07-18) Milestone 6 desktop polish complete: console rows copy exactly through `Ctrl+C` or a context action, right-click selects the pointed row, clipboard failures are no-ops, and redirected dotnet/MSBuild output is decoded explicitly as UTF-8 so localized diagnostics remain readable.
- [x] (2026-07-18) Milestone 7 complete: the stable Avalonia launcher selects an exact-SDK installation/editor, persists recent projects outside games, and follows a strict bounded exit-code-20 handoff; Tooling 40/40, Launcher 12/12, Packager 15/15, and Editor 88/88 non-opt-in tests pass, with one external editor smoke skipped by default.
- [ ] Milestone 8A: engine module composition from installed payload.
- [x] Milestone 8B: full external runtime — server + two clients, ECS, content, snapshot, and hot reload on two independently generated games; the editor switch is verified separately by Remediation R3.
- [x] Milestone 8C: remove editor-local runtime packaging.
- [ ] Milestone 8D: remove MyGame and repository-local composition roots.
- [ ] Milestone 8E: decouple Configurator from game profile.
- [ ] Milestone 8F: final acceptance and documentation.
  - [x] (2026-07-21) Milestone 8B-8D remediation audit reproduced the first regression with the ordinary SDK integration suite: `Template_has_the_standard_external_game_structure` fails because Milestone 8D deleted `Source/KarpikGame.Shared/Content/shared-runtime.txt`. The audit also found that the alleged 8B project switch was two independent runs, the 8C opt-in preview tests still consume the deleted editor-local runtime layout, `Mods/` is not published into game bundles, and the uncommitted `KARPIK_CONTENT_ROOT` workaround bypasses the validated bundle boundary.
  - [x] (2026-07-21) The developer approved the bounded remediation design: retain a representative external ECS/content/mod sample rather than porting the deleted platformer; make all runtime assets bundle-owned; prove switching through the real editor coordinator; and finish only the Configurator cleanup required to leave 8D internally consistent.
  - [x] (2026-07-21) Remediation R1 complete (`aeabe44`, `197c357`, `561f17e`; task re-review clean): restored bundle-owned Content fixtures and installer reads; added optional, bounded, transactional `Mods/` publication with nested paths preserved by two-phase MSBuild `Include`/`Update`; removed the source-content fallback; and corrected renamed-project acceptance paths. SDK task tests pass 41/42 with one explicit Windows symlink-privilege skip, ordinary SDK integration passes 5/7 with two opt-in skips, and the exact external runtime acceptance passes 1/1 outside the sandbox with both independently generated games, ECS state preservation, content, and hot reload.
  - [x] (2026-07-22) Remediation R2 complete (`5426dfe`, `00d8066`, `5d6a819`, `8d4ee33`; task re-review approved): preview opt-ins now require validated external engine/game roots, discover exact Runtime Client/Server bundle paths through MSBuild evaluation, and explicitly skip when absent; resolver fixtures physically separate installed runners from active-game bundles; editor-local runtime packaging has a source contract; and project opening rejects foreign, linked, and dangling-linked bundle paths while allowing ordinary not-yet-created evaluated outputs. The R2 filter passes 14 tests with two explicit opt-in skips; ProjectOpenService passes 17 tests with two explicit Windows symlink-privilege skips.
  - [x] (2026-07-22) Remediation R3 complete: the real `EditorShellViewModel.OpenProjectAsync`/`ProjectSwitchCoordinator` smoke now starts one server plus two clients for each external game, proves all three old PIDs have exited when the second project is published, verifies exact game-bundle and installed-runner ownership, and proves empty reload state/shadow trees plus exclusive module-file access after both switching and shutdown. Existing production teardown already satisfied the strengthened contract, so no production change was required. Targeted lifecycle tests pass 33/33; the exact opt-in smoke passes 1/1 in 26 s outside the sandbox with the freshly published `0.6.0-dev-d43a558206c63eb750e4f09c02877e7c3f936e10bfad2aa9c6f69ec34b1996ed` payload.
  - [ ] Remediation R4: finish the Configurator/game-root cleanup required by 8D, regenerate artifacts, and reconcile milestone evidence.
  - [x] (2026-07-18) Milestone 8A: the editor passes an explicit installed engine root to the runner; payload layout v2 carries a canonical side-aware module catalog; one collectible context composes compatible engine modules with game assemblies while Core/Runner/Dragon/Karpik.Jobs remain identity-shared. Worker startup revalidates the immutable installation hash and confirms that the running side-specific Runner belongs to that installation; byte-distinct duplicate identities and multiple versions of one assembly name are rejected before publication; dependency binding requires an exact identity; and native probing uses the installation-owned root plus the current RID. Runner 98/98, Tooling 41/41, Packager 17/17, Configurator 9/9 plus generated-artifact validation, editor runtime resolution 5/5, and the fresh layout-v2 installed-runner external snapshot smoke 1/1 pass without the former missing `EcsDefaultWorld` service error.
  - [x] (2026-07-19) Milestone 8B standalone runtime acceptance: the external server-plus-two-clients smoke passes with ECS state preservation, content reading, and client output collection for each of two independently generated games. Source changes: native layout aligned (`Path.PathSeparator` replaces `";"`, `native/<rid>/` probing, packager strips `native/` prefix); `ServerGameInstaller`/`ClientGameInstaller` removed `[DI]` field injection and read content at startup; the test verifies `TotalEntityCount` grows by exactly 1 after reload, `GameComponent(42)` is present before and after reload, client outputs contain no crash, and both games complete the same standalone lifecycle. Sequential completion is not editor-switch evidence; the real coordinator switch is covered by Remediation R3.
  - [ ] Milestone 8C: editor-local runtime packaging is removed after 8B replacement coverage passes.
  - [ ] Milestone 8D: the reusable sample is moved into the external template and repository-local `MyGame`, `ClientLauncher`, and `ServerLauncher` composition roots are removed.
  - [ ] Milestone 8E: Configurator emits an engine SDK payload module catalog without `MyGame` or repository game-profile assumptions.
  - [ ] Milestone 8F: complete automated acceptance, two-SDK desktop smoke, documentation, and Graphify refresh pass.

## Surprises & Discoveries

- Observation: The existing Configurator is already a useful source of module graph rules, but `Configurator/RepositoryParser.cs` hard-codes both `Modules/` and four `MyGame/` paths and reads the module profile from the engine root `Directory.Build.props`.
  Evidence: `RepositoryParser.GameRootPaths`, `RepositoryParser.Load`, and `GraphValidator.ValidateConventions`.

- Observation: The current editor packages client and server runtime trees into `Karpik.Editor/bin/.../runtimes` by building repository-local `ClientLauncher` and `ServerLauncher` projects and copying `MyGame` content.
  Evidence: `Karpik.Editor/EditorRuntimeBundles.targets`.

- Observation: The current editor model persists a directory path and accepts any existing directory; it does not evaluate `.slnx`, `global.json`, project SDK identity, kind, side, or module graph.
  Evidence: `Karpik.Editor/Models/EditorWorkspace.cs` and `EditorShellViewModel.OpenProject`.

- Observation: Adding any project to `KarpikEngine.slnx` intentionally adds it to `Generated/KarpikModuleCatalog.props`; the catalog is a solution-wide id/path map, not only a runtime module list.
  Evidence: `Configurator/ArtifactGenerator.cs:BuildCatalog` and the accepted Milestone 1 generation diff.

- Observation: Raw XML safety requires location-aware parsing even without MSBuild evaluation; scanning arbitrary descendants can mistake target-time content for static declarations.
  Evidence: Milestone 1 review and regression tests in `Karpik.Engine.ProjectModel.Tests/GameSolutionValidationTests.cs`.

- Observation: Repository `bin/` directories are not a trustworthy payload input because they retain stale and configuration-dependent files, so two otherwise identical packaging runs can produce different payload hashes.
  Evidence: The initial real repository smoke changed from 393 to 401 files on rerun; the repository-mode stale-output regression failed until every selected project was restored and built below a transaction-owned `ArtifactsPath`.

- Observation: Deterministic managed compilation does not by itself make `dotnet pack` byte-reproducible; NuGet emits a random core-properties part name and ZIP metadata.
  Evidence: The two-build regression narrowed the last delta to `_rels/.rels` and the generated core-properties path after all DLL/PDB deltas were removed.

- Observation: `ArtifactsPivots` is not safe to capture into an early global property used by NuGet pack items.
  Evidence: The synthetic repository built SDK tasks below `artifacts/bin/Karpik.Engine.Sdk.Tasks/release`, while the late property expanded to an empty pivot and `NU5019` searched one directory too high.

- Observation: `Kill(entireProcessTree: true)` requests termination but does not prove it completed, and immediately deleting the owned build workspace can race a still-live child.
  Evidence: The injectable never-exiting process regression reaches the execution timeout, records one tree-kill request, times out a second bounded exit wait, and keeps its marked staging directory across recovery.

- Observation: Flattening complete module outputs into one `modules/` directory is invalid because unrelated modules can carry different assemblies with the same filename.
  Evidence: The first post-refactor complete-repository run failed on a real `modules/Newtonsoft.Json.dll` collision. A synthetic two-module regression then reproduced the issue with byte-distinct `SharedDependency.dll` files and passes only when each module owns an isolated `modules/<module-id>/` subtree.

- Observation: The final isolated-module complete-repository payload is deterministic and reusable.
  Evidence: Two complete-repository packager runs produced and then reused `0.6.0-dev-5270fb65f53bc14fd7cba44424975bab7fb1ede51ee17ca574a6fd09532bb185`; an independent implementation reproduced the same framed SHA-256 over 526 files and found no staging or replacement residue.

- Observation: An unrestricted solution restore on this high-core-count Windows host spawned many MSBuild workers and could stall while probing unavailable package sources; `Kill(entireProcessTree: true)` could itself block while enumerating that tree.
  Evidence: The integration harness now uses a compact temporary offline feed seeded from its already-resolved package closure, package-source mapping that forces `Karpik.Engine.Sdk` to the freshly packed local feed, single-node restore flags, disabled node reuse, and separately bounded tree-kill and exit-confirmation waits. The accepted run left no worker or temporary directory behind.

- Observation: A game bundle cannot double as the runner installation directory because the existing Bootstrap resolved `Karpik.Engine.Core.Runner.dll` from `Directory.GetCurrentDirectory()`.
  Evidence: The first real-process external launch connected the new bundle working directory to the legacy lookup and failed at `Bootstrap.cs`; constructor injection of `IEngineRunner` removed the disk lookup, and the isolated-working-directory regression now passes.

- Observation: Named pipes are denied by the agent filesystem/process sandbox on this Windows host even for a unique same-process server/client pair.
  Evidence: The minimal reproduction and focused IPC test fail in-sandbox with Win32 `0x80070005`, while the unchanged test passes outside the sandbox. All runner and real-process acceptance gates that depend on IPC were therefore executed outside the sandbox.

- Observation: The retained Milestone 3 engine payload predates the explicit-bundle runner boundary.
  Evidence: Integration creates a new validated content-addressed installation from the retained payload's immutable editor/SDK/module/native trees plus the current complete Release runner output; it never mutates the retained installation.

- Observation: Runtime content must be collected from `$(TargetDir)Content/**` after referenced-project copy, not directly from each side project's source tree.
  Evidence: The Shared template content is copied transitively into both side outputs and appears in both bundles; source-tree collection omitted it.

- Observation: A successful hot-reload state response already asks the worker loop to stop, so a subsequent unconditional shutdown message races the pipe teardown.
  Evidence: The first real restart acceptance received the empty state and then failed with `Pipe is broken`; waiting for state-driven exit before conditionally sending shutdown makes both final external runs pass.

- Observation: Current .NET MSBuild can emit bounded machine-readable evaluation results without loading MSBuild into the editor process.
  Evidence: `dotnet msbuild -getProperty:... -getItem:ProjectReference -getResultOutputFile:<path>` returns JSON; the Milestone 6A real-child regression verifies exact properties and `FullPath` item metadata.

- Observation: Path rechecks before MSBuild do not close replacement races between raw validation and evaluation.
  Evidence: Milestone 6A now parses through retained Windows no-follow handles and keeps file/directory sharing leases until MSBuild exits; the deterministic hook cannot rename, rewrite, or rename the project directory. Platforms without mandatory directory sharing evaluate only an owned bounded mirror of build metadata and remap mirror-root paths back to the original solution.
- Observation: A bounded foreground termination attempt is not proof of process exit; releasing the process wrapper, result path, or project-input lease after a blocked `Kill` or expired exit confirmation reopens the exact input/mirror race the lease was designed to close.
  Evidence: The deterministic reaper regression blocks `Kill`, lets the first exit confirmation expire, verifies that the portable mirror and result remain owned after the inspector returns, then signals exit and observes deferred process/result/mirror cleanup. A separate capacity test proves another process is rejected before launch while the one-slot reaper is occupied.

- Observation: The first Milestone 6B draft compiled and passed the pre-existing 62 editor tests while bypassing the entire Milestone 6A production path: `.slnx` open called a directory-only method, `MainWindow` never created `ProjectSwitchCoordinator`, runtime paths still came from `AppContext.BaseDirectory`, and build/publish processes were not owned by `IActiveProjectLifetime`.
  Evidence: No editor test referenced `EditorShellViewModel.OpenProjectAsync`, `EditorProjectLifetime`, or the new build/publish commands; direct call tracing from `MainWindow` ended in the legacy `ProjectPath` assignment instead of `ProjectOpenService.OpenAsync`.

- Observation: A retained stable-name development payload can be obsolete even when a newer hash-qualified payload for the same SDK is valid.
  Evidence: The first Milestone 6 external switch smoke rejected `artifacts/karpik-home/Engines/0.6.0-dev` because it still flattened module DLLs; selecting the sole payload that passes `EngineInstallationValidator` made the unchanged two-game smoke pass.

- Observation: Returning a non-zero Avalonia desktop lifetime code is insufficient when the process entry point discards the lifetime return value.
  Evidence: The first Milestone 7 exit-contract test observed `void Program.Main`; changing both desktop entry points to `int Main` makes editor handoff code `20` observable by the launcher process host.

- Observation: Expected-SDK validation cannot safely classify a cross-version handoff before proving the current installation is generally valid.
  Evidence: A missing `.complete` marker was initially masked by the earlier `WrongSdkVersion` result; validating the installation first and comparing its proven manifest second preserves corrupt-installation failures.

- Observation: isolated module outputs initially contained both `Newtonsoft.Json` 9.0.1 and 13.0.4 because `NativeLibraryLoader` supplied an old transitive dependency.
  Evidence: a single collectible `AssemblyLoadContext` cannot load both versions deterministically. The Veldrid projects now pin 13.0.4, and validation rejects both byte-distinct copies of one full identity and different identities sharing one simple assembly name before publication.

- Observation: Milestone 8D deleted the three `runtime.txt` fixtures and the installer reads that Milestone 8B used as its content acceptance contract, but left the tests and Progress claim unchanged.
  Evidence: `dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --no-restore` on 2026-07-21 produced 1 failed, 4 passed, and 2 skipped; the failure is the missing `Source/KarpikGame.Shared/Content/shared-runtime.txt`.

- Observation: the Milestone 8B test named a second independent materialization a project switch, but it never called `ProjectSwitchCoordinator` or `EditorShellViewModel.OpenProjectAsync` while workers from the first project were active.
  Evidence: this was the historical audit state on 2026-07-21: `ExternalGameCliTests.AssertMultiWorkerEcsCycleAsync` fully stopped each game before the second game was materialized, while the coordinator-based smoke started only one client. Remediation R3 now keeps one server and two clients active in each game and proves teardown through the real editor switch.

- Observation: the SDK copies `Mods/**` to side-project outputs but `BuildKarpikRuntimeBundle` collects only `$(TargetDir)Content/**`, so the moved Lua sample is absent from installed runtime bundles.
  Evidence: `templates/Karpik.Game/Source/KarpikGame.Client/KarpikGame.Client.csproj`, `templates/Karpik.Game/Source/KarpikGame.Server/KarpikGame.Server.csproj`, and `Karpik.Engine.Sdk/Sdk/Sdk.targets` disagree about the published asset roots.

- Observation: the original 8D/8E order is not independently executable because deleting `MyGame/` first makes the pre-8E Configurator model invalid, while performing 8E first changes generated composition artifacts before 8D.
  Evidence: commit `a420207` necessarily removed `RepositoryParser.GameRootPaths` and `ArtifactGenerator` game roots inside the nominal 8D commit, but left `RepositoryModel.GameRoots`, test-fixture game roots, and launcher wording in generated output.

## Decision Log

- Decision: Use a thin NuGet-distributed custom MSBuild SDK plus a separate versioned engine payload.
  Rationale: MSBuild SDK resolution keeps games compatible with ordinary `dotnet` commands, while the payload can carry runners, native libraries, editor binaries, and modules that do not fit a managed package-only model.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Require `Karpik.Engine.Sdk` on every project in a game `.slnx` and require orthogonal `KarpikProjectKind` and `KarpikSide` properties.
  Rationale: Tests, tools, generators, assets, and runtime projects need different build profiles but must not bypass analyzers or side-boundary validation.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Do not introduce a `.karpik` manifest or require a game-level `Directory.Build.props` contract.
  Rationale: `.slnx`, `global.json`, and SDK-based `.csproj` files already provide a standard .NET project model and avoid duplicated state.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Use a stable launcher with an editor packaged per compatible engine installation.
  Rationale: The current editor directly references engine contracts; version-matched editors avoid immediate compatibility branches across historical APIs.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Repair Milestones 8B-8D around a representative external sample instead of porting the deleted repository platformer wholesale.
  Rationale: the accepted external contract is server plus two clients, ECS state, content, mods, snapshot, hot reload, and project switching. Porting the old network/physics/rendering sample would require generalizing `Network.Codegen` and module selection and is a separate feature rather than a correction of the external-project migration.
  Date/Author: 2026-07-21 / developer and Codex

- Decision: Runtime `Content/` and `Mods/` remain immutable game-bundle inputs; the editor must not redirect asset lookup to a mutable game source directory.
  Rationale: bundle publication already provides bounded path, link, size, identity, and transactional validation. `KARPIK_CONTENT_ROOT` would introduce an unvalidated third runtime root, make CLI and editor launches behave differently, and weaken project-switch isolation.
  Date/Author: 2026-07-21 / developer and Codex

- Decision: Treat the minimum Configurator decoupling needed to remove `MyGame/` as an 8D prerequisite and leave broader generator modernization to later work.
  Rationale: the former milestone order is circular. Removing the dead `GameRoots` model and deleted-launcher diagnostics restores a coherent engine-only graph without expanding this remediation into a network-codegen redesign.
  Date/Author: 2026-07-21 / developer and Codex

- Decision: payload layout v2 requires `modules/modules.catalog`, with canonical `Shared|Client|Server` ownership for every isolated module root.
  Rationale: the external runner cannot safely infer side ownership after packaging from module IDs alone, and flattening isolated outputs reintroduces dependency collisions. A hashed installation-owned catalog makes selection deterministic and keeps engine modules out of game-owned bundles.
  Date/Author: 2026-07-18 / Codex

- Decision: Use a strict one-shot JSON handoff file plus process exit code `20`, with `KarpikEngineRoot` inherited by the selected editor.
  Rationale: The launcher can validate an absolute existing `.slnx`, resolve its exact SDK again, and bound restart loops without sharing mutable in-process state across editor versions. Handoff preflight runs before active-project teardown, but exit `20` is emitted only after teardown completes successfully.
  Date/Author: 2026-07-18 / Codex

- Decision: Preserve one active project per editor and one server plus multiple clients within that project.
  Rationale: This isolates workers, IPC, ports, watchers, logs, hot-reload state, and build state while preserving the existing multisession editor workflow.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: New tooling projects added to `KarpikEngine.slnx` receive normal deterministic entries in `Generated/KarpikModuleCatalog.props`.
  Rationale: Configurator intentionally catalogs every solution project. Excluding the new projects would require a special case and contradict that existing contract. `AutoGenerated.targets` and `Generated/ModuleLoader.cs` remain unchanged in Milestone 1.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Ship a package-owned `Sdk/Solution.targets` and copy its standard `Directory.Solution.targets` importer into each external game root.
  Rationale: The generated `.slnx` metaproject must reject independent foreign projects before it launches any child project. The SDK import stays pinned by the same `global.json` mapping and does not introduce a custom manifest or game-local validation implementation.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Require every game graph edge to be an unconditional literal top-level `<ProjectReference Include="..." />` in the referencing `.csproj`, and require the normalized evaluated direct-reference set to match that raw set exactly.
  Rationale: The editor and CLI need one deterministic graph that can be inspected safely without evaluating child projects or loading game assemblies. Imported, conditional, expression-based, globbed, and target-mutated edges can diverge from the raw model, bypass solution membership, or hide cycles. Every Karpik project therefore uses distinct task invocations before NuGet's recursive restore walk and at the final point before `AssignProjectConfiguration`, then validates the complete raw transitive graph. Both targets are declared after the Microsoft SDK targets import so earlier consumer/`Directory.Build.targets` restore mutations are visible; the late target remains last so consumer/imported build mutations are visible before reference resolution.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Build repository-mode payload inputs only into a transaction-owned artifacts root and copy only explicit editor, runner, selected-module, SDK-package, and native-runtime outputs.
  Rationale: Clean owned outputs prevent stale source-tree binaries, old module-version directories, and unrelated launch artifacts from entering an installation. The same generic engine runner output is used for both client and server until later milestones introduce game-owned bundles.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Canonicalize the SDK `.nupkg` after `dotnet pack` by sorting entries, fixing ZIP timestamps, and normalizing the NuGet core-properties part and relationship identifiers.
  Rationale: The engine payload hash covers package bytes. NuGet's random OPC part name would otherwise force an atomic replacement on every identical build even though every package entry payload is semantically unchanged.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Frame the payload content hash with a format tag, file count, normalized UTF-8 path lengths, content lengths, and bytes while excluding only the root manifest and completion marker.
  Rationale: Explicit framing prevents ambiguous concatenations and makes the independently reproducible hash contract stable without a self-referential manifest hash.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Engine installations are immutable after validation; publish a candidate with one rename to a previously absent destination and never move a valid destination away.
  Rationale: A two-rename replacement creates a Windows interval in which a known-good installation name is absent. Stable-version differing content is an immutable-version conflict. Development versions use `<engine-version>-<full-content-hash>`, so differing valid payloads coexist and exact-SDK resolution reports sorted ambiguity candidates.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Build SDK tasks first and pass the exact transaction-owned `KarpikSdkTasksOutputPath` to `dotnet pack`.
  Rationale: The pack item needs a fully evaluated directory. Capturing the late `ArtifactsPivots` property in an earlier global property silently produced the wrong path.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: A timed-out packager subprocess must be killed, awaited again with a separate bound, and have redirected output drained only after exit is confirmed.
  Rationale: If termination cannot be confirmed, a typed error preserves and marks the owned staging directory; ordinary recovery skips that marker rather than deleting files beneath a potentially live child.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Store each selected module's clean build output below `modules/<module-id>/` and require `modules/<module-id>/<module-id>.dll` as its primary assembly.
  Rationale: Module dependencies can legitimately share filenames while containing different bytes. Isolated subtrees preserve each module's dependency closure without collision; the later hot-reload loader must enumerate these roots recursively rather than assume a flat module directory.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Test the external template through an isolated template hive and a temporary hermetic NuGet configuration containing the freshly packed SDK feed plus a compact copy of the integration project's resolved test-package closure.
  Rationale: The test must neither mutate global template/NuGet state nor depend on network availability, while package-source mapping must guarantee that `Karpik.Engine.Sdk/0.6.0-local` comes from the current repository package rather than a stale cache. Single-node restore is required on this host to prevent solution restore from leaving a large worker tree.
  Date/Author: 2026-07-15 / Codex

- Decision: A runtime bundle is a game-owned, side-marked, versioned directory with one completed `modules.version.*` manifest and non-empty `Content`; publication uses a unique owned sibling staging directory and restores the last proven complete bundle on any visibility or marker-finalization failure.
  Rationale: The build must never expose a partial tree, delete an unproven user directory, or copy an engine runner into mutable game output. Exact markers and a sorted assembly manifest give the runner a deterministic startup contract without loading assemblies during the build.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Runtime launch uses immutable `RuntimeLaunchOptions` carrying side, engine-installed runner path, and game bundle path; Bootstrap receives its `IEngineRunner` through constructor injection.
  Rationale: Executable ownership, working-directory ownership, and engine composition are independent boundaries. Explicit inputs remove `AppContext.BaseDirectory`, current-directory, and manual runner-assembly loading from external startup and keep reload state/shadow cleanup within the bundle.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: The generated parameterless `ModuleLoader` and repository `MyGame` lists remain only obsolete monorepository compatibility until Milestone 8; explicit bundle construction never consults them or falls back to the runner directory.
  Rationale: Milestone 5 must make external launches strict without prematurely deleting repository composition roots scheduled for migration. A collectible bundle-scoped load context proves the new path releases shadow resources independently.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: SDK publication and runtime validation share a canonical bounded bundle contract: 32,768 tree entries, depth 64, a 1 MiB/4,096-entry manifest, 4 GiB individual files, and 32 GiB aggregate bytes; exact UTF-8/LF markers and manifest bytes, exact root/module shape, and the full ancestor reparse chain are mandatory.
  Rationale: Recovery and cleanup may move or delete only fully proven owned trees, and hostile or malformed trees must be rejected before unbounded allocation, enumeration, or source copying. A cross-contract test prevents SDK/Core constant drift.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Milestone 5 real hot-reload acceptance serializes and consumes an empty `ModuleStates` payload; non-empty ECS/module state is deferred to Milestone 8.
  Rationale: The explicit external game bundle intentionally contains only game Client/Server+Shared assemblies and no engine `ECSInstaller` compile contract. The current milestone can still prove request/response, old-process exit, bundle-owned state file, same-bundle restart, new PID/readiness, state consumption, and cleanup without expanding the SDK boundary prematurely.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Collectible module shadow cleanup is two-phase.
  Rationale: An `AssemblyLoadContext` cannot be proven collected while its `Dispose` stack may still root loaded assemblies. Dispose unloads and enqueues a bounded owned-shadow record; the outer runner/standalone caller performs bounded GC polling and exact contained deletion after the stack returns, with ProcessManager cleanup as defense in depth.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Request/response IPC and worker replacement use explicit single-owner lifetimes.
  Rationale: A response handler must exist before its request can be observed and must be removed through `finally`; only one reload may own the worker transition, every exit waiter captures one immutable `Process`, and a force-killed worker must confirm exit before its handle is disposed or a replacement starts.
  Date/Author: 2026-07-15 / developer and Codex

## Outcomes & Retrospective

Milestone 2 produced a NuGet-resolved `Karpik.Engine.Sdk` with package-owned project, restore-walk, late-build, and solution hooks. The task suite passes 16/16 and ProjectModel passes 31/31. Structural diagnostics carry typed reasons, so task-level duplicate suppression uses reason and path rather than localized message text. Package inspection found each required SDK, solution-hook, template, task, and ProjectModel assembly exactly once. External plain-`dotnet build` smokes prove that an independent foreign project fails with `KARPIK001` before compilation in both solution project orders; imported same-side edges fail with `KARPIK004` whether their target is present in or omitted from `.slnx`; a conditionally omitted raw edge fails with `KARPIK004`; a cold direct literal cycle fails during restore with `KARPIK006` before NuGet's `MSB4006`; a `Directory.Build.targets` restore-walk mutation fails with `KARPIK004` before NuGet graph traversal; and inline/imported target-time mutations fail with `KARPIK004` before `AssignProjectConfiguration`, reference resolution, or compiler diagnostics. The corresponding valid solution builds normally, and Configurator validation remains green. `graphify update .` was attempted but the local Windows graph rebuild failed with access denied; its partial cache edit was restored and excluded from the milestone commits.

Milestone 3 adds strict six-field installation manifests, exact-SDK resolution from `global.json`, platform-local installation lookup with explicit-root precedence, deterministic framed content hashing, symlink/reparse-safe traversal, validation diagnostics, immutable installation publication, and owned staging/legacy-recovery handling. Stable versions reject any differing generally valid content without moving it, even when the requested SDK differs; development payloads use full-hash-qualified names and coexist. Production legacy recovery validates both candidate and backup, restores only a proven backup, never exposes an invalid/exceptional/unproven absent-destination backup, and retains evidence when neither side is proven. The packager accepts either a prepared payload or the repository, produces the exact editor/SDK/runner/isolated-module/native layout, and reuses a byte-identical installation. Tooling passes 32/32 tests and Packager passes 12/12; the relevant Release build has zero warnings/errors and Configurator validation passes. A synthetic repository invoking real `dotnet restore`, `build`, and `pack` twice proves byte-identical output and reuse, verifies the SDK package contains owned task and ProjectModel assemblies, ignores arbitrary stale source `bin/` and module-version directories, and preserves byte-distinct same-named dependencies in isolated module subtrees. Bounded process tests prove unconfirmed termination preserves marked staging across recovery. Two final complete-repository runs published and then reused `0.6.0-dev-5270fb65f53bc14fd7cba44424975bab7fb1ede51ee17ca574a6fd09532bb185`; an independent framed SHA-256 audit reproduced that hash over 526 files, verified 22 valid module roots, and found no staging or replacement residue. Milestone 3 is complete.

Milestone 4 adds a standard installable `karpik-game` template with Client, Server, Shared, and Client-sided Test projects, a source-name-safe `.slnx`, the pinned SDK `global.json`, and the package-owned solution hook importer. No generated source file contains `.karpik`, game-level Directory.Build files, an engine-source reference, or a machine path. The opt-in integration suite validates the retained Milestone 3 payload, packs the current SDK, installs and materializes through isolated temporary state, and passes restore, build, test, and client publish outside the repository. Two clean non-default-name materializations prove deterministic source-name replacement by comparing every normalized path and file byte. Fresh mutation fixtures prove solution-level `KARPIK001`, solution-level `KARPIK005`, and direct-project `KARPIK005` all occur before compiler markers or runtime diagnostics. The required suite passes 4/4 and Configurator validation passes. Milestone 4 is complete; no runtime bundle or runner-start behavior from Milestone 5 was introduced.

Milestone 5 moves mutable runtime ownership into Runtime Client/Server build outputs. The SDK task publishes deterministic Client+Shared and Server+Shared bundles through owned staging, exact versioned markers, rollback, reparse/traversal checks, and a sorted assembly manifest; Shared and other non-runtime projects do not run the target, and engine runner binaries are rejected. `RuntimeLaunchOptions`, strict runner arguments, explicit Bootstrap injection, and generated bundle-scoped module loading separate the immutable engine runner from the game working directory. Module shadow copies, reload state, ready payloads, and cleanup remain under the bundle; state-file arguments are absolute, existing, reparse-safe, and contained below `reload/state`. SDK task tests pass 26/26, runner tests pass 53/53 outside the sandbox, Configurator tests pass 9/9 with byte-for-byte generated validation, and Core/Runner Release builds have zero warnings/errors. Three opted-in external gates passed, including two consecutive idempotence runs and a final current-tree run; each built side-pure bundles outside the repository, started a runner from a newly validated versioned engine installation, reached IPC-ready, stopped through IPC, and exited cleanly. `graphify update .` was attempted but failed with Windows access denied; its sole partial cache edit was restored and excluded. Milestone 5 is complete; editor switching, launcher handoff, and removal of obsolete monorepository compatibility remain in Milestones 6-8.

The Milestone 5 review hardening makes proof strict rather than marker-only. Both publisher and runtime reject noncanonical UTF-8/LF manifests, unlisted or missing DLLs, unexpected roots/modules, runner binaries, links anywhere in the ancestor/tree chain, and bounded-count/depth/size violations. All source sizes, generated bytes, destinations, and collisions are preflighted before any staging copy. Runtime content comes from post-build `TargetDir`, so Shared content is present in both side bundles. Ready cleanup accepts only the exact uniquely resolved module directory. The generated explicit loader validates before copying, preserves bounded recursive legacy copying, and performs two-phase standalone shadow cleanup. The external harness restores/builds the runner entirely below transaction-owned `ArtifactsPath` and `MSBuildProjectExtensionsPath`, hashes repository runner `bin/obj` and the engine installation to prove no mutation, then performs a real empty-state hot reload with old/new PID, second readiness, state consumption, and clean shadow/stop assertions. Final evidence is SDK task tests 35/35, runner tests 67/67 outside the sandbox, Configurator tests 9/9 plus generated validation, and two final opted-in external passes (1m58s and 1m54s).

The Milestone 5 re-review closes the remaining lifecycle and replacement gaps without entering Milestone 6. `IpcServer` serializes state/shutdown requests, subscribes before sending, releases handlers in `finally`, and shares one framed-write gate across request types; the worker client applies the same write serialization. Once a state or shutdown request is transmitted, cancellation is deferred until the worker transaction reaches a stable restart/stop boundary, while lifetime cancellation drains active waits without disposing their gates underneath them. `ProcessManager` uses an interlocked reload owner, captured-process exit waits with unconditional handler cleanup, and confirmed termination before dispose/restart/stop. Build publication proves the nearest existing destination ancestor before creating missing parents, treats ownership/completeness independently from the incoming primary name while requiring that primary in the newly staged manifest, and uses case-insensitive portable bundle identities. `KarpikRuntimeBundlePath` is now an overrideable evaluated property declared after the Microsoft SDK targets import, and generated explicit loading removes a shadow created before a copy failure. Fresh evidence is SDK task tests 38/38, runner tests 75/75 outside the sandbox, Configurator tests 9/9 plus generated validation, Core and Runner Release builds with zero errors, two consecutive opted-in external passes (2m07s and 2m16s), and one final exact-tree pass (2m06s). `graphify update .` again failed with Windows access denied; its sole partial stat-cache modification was restored and excluded.

The final Milestone 5 lifecycle audit serializes every `ProcessManager` transition behind one gate. Worker-originated and public reload use the same gate and a private non-reentrant start core; counted stop requests and disposal publish intent before waiting, and the process-launch commit checks that intent under the same lock, so queued starts and in-flight reloads cannot launch after teardown begins. Lifecycle callbacks run outside the transition gate so callback-initiated disposal cannot reenter it. Each worker also owns a locked exit-notification disposition: reload defers exit publication before sending `StateRequest`, suppresses it only after state acquisition succeeds, and republishes an already deferred exit if acquisition aborts. The real minimal IPC worker exits immediately after `StateResponse`, matching production ordering and proving worker-requested reload racing stop/dispose, direct public reload racing stop, callback-to-dispose reentrancy, queued start/stop intent, and planned old-exit suppression. Focused lifecycle tests pass 8/8 and the full Runner suite passes 81/81 outside the sandbox; the exact-final opted-in external RuntimeBundle restart passes 1/1 in 1m46s. An independent narrow re-review reports Ready with no Critical or Important findings. This remains Milestone 5 process lifecycle work; editor project switching remains Milestone 6.

Milestone 6 now has one production composition root from Avalonia startup to raw/evaluated project validation, candidate publication, project-owned sessions and dotnet commands, and ordered shutdown. Opening and switching accept `.slnx` files only; build and publish use the evaluated solution/project paths; no editor-local runtime bundle fallback remains. Every session mutation shares the coordinator command gate, and deferred session output, state, selection, and snapshots carry the project generation. Workspace teardown preserves current window/panel state while changing only the solution identity. The dotnet command owner drains and bounds UTF-8 output, kills the complete process tree, confirms exit, and disposes even when output capture fails. Console rows can be copied exactly through the keyboard or context menu. Fresh evidence is Editor 82/82 with the opt-in test skipped by default, a successful editor build, and an outside-sandbox two-game smoke that builds both games with ordinary dotnet commands, starts two workers for the first game, confirms both exit before the second publishes, verifies distinct second-game bundle roots, and confirms its worker exits on shutdown. Milestone 6 is complete; launcher selection and cross-version handoff remain Milestone 7.

Milestone 7 adds a stable Avalonia launcher over the existing exact-SDK installation resolver. Recent projects live below platform local application data; corrupt history degrades to an empty list with a visible diagnostic. Each launch receives a unique owned handoff path and the selected `KarpikEngineRoot`; normal exit returns to project selection, code `20` requires a strict validated request, all other non-zero codes stop, and handoff loops are bounded. The editor preflights version compatibility before mutating its active context, performs the complete old-project teardown before returning a prepared incompatible project, and propagates the Avalonia lifetime code from `int Main`. Payload validation now requires `editor/Karpik.Editor.dll`. Automated two-installation tests prove exact editor selection, normal exit, malformed handoff rejection, bounded restarts, recent deduplication, and corrupt-cache recovery. Fresh evidence is Tooling 40/40, Launcher 12/12, Packager 15/15, Editor 88/88 with one opt-in smoke skipped, and zero-warning launcher builds.

## Context and Orientation

`KarpikEngine.slnx` currently contains engine libraries, first-party modules, editor projects, engine runners, Configurator, repository-local launchers, and `MyGame`. `Directory.Build.props`, `Directory.Build.targets`, `AutoGenerated.targets`, `Plugins.targets`, and `Generated/KarpikModuleCatalog.props` cooperate to turn shorthand `KarpikModuleDependency` items into project references and runtime plugin lists.

`Configurator/RepositoryParser.cs` parses the engine solution and raw project XML. `Configurator/GraphValidator.cs` validates module selection, dependency activation, cycles, and side boundaries. `Configurator/ArtifactGenerator.cs` generates repository-level build artifacts. These engine-repository responsibilities must remain available, but the reusable game-solution vocabulary and side rules must move into tooling that can ship with `Karpik.Engine.Sdk`.

`Karpik.Editor/EditorRuntimeBundles.targets` currently builds `ClientLauncher/ClientLauncher.csproj` and `ServerLauncher/ServerLauncher.csproj`, then copies their outputs and `MyGame/MyGameResources` below the editor output. `Karpik.Editor/Runtime/RuntimeBundleResolver.cs` consequently resolves bundles relative to `AppContext.BaseDirectory`. This is the primary ownership inversion to remove: engine runners belong to an engine installation, while game assemblies and content bundles belong to game build output.

`Karpik.Editor/ViewModels/EditorShellViewModel.cs` currently stores one folder in `ProjectPath`, owns one long-lived `EditorSessionManager`, and constructs backends from editor-local bundles. The new project context must instead own its build inspection, resolved installation, runtime bundles, watchers, and session manager. Switching projects destroys the complete old context before publishing the new one.

In this plan, an **MSBuild SDK** is a NuGet package containing `Sdk/Sdk.props` and `Sdk/Sdk.targets` that MSBuild resolves before evaluating a project. An **engine payload** is an installed directory containing runtime assemblies, runners, modules, native libraries, a compatible editor, and an installation manifest. A **game bundle** is side-specific output produced by a game project and loaded by an engine-owned runner.

## Planned File Structure and Interfaces

Create these focused units rather than extending Configurator or `EditorShellViewModel` into additional monoliths:

- `Karpik.Engine.ProjectModel/`: raw `.slnx` and `.csproj` parsing, project kind/side vocabulary, diagnostics, and game graph validation. It has no Avalonia, runtime, or MSBuild task dependency.
- `Karpik.Engine.ProjectModel.Tests/`: temporary-solution unit tests for parsing, SDK participation, side rules, cycles, and project kinds.
- `Karpik.Engine.Sdk.Tasks/`: MSBuild task adapters over `Karpik.Engine.ProjectModel` and bundle-publication tasks. It references `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` with runtime assets kept inside the SDK package.
- `Karpik.Engine.Sdk.Tasks.Tests/`: task-level tests with fake build engines and temporary project graphs.
- `Karpik.Engine.Sdk/`: pack-only project containing `Sdk/Sdk.props`, `Sdk/Sdk.targets`, package metadata, and the task assemblies.
- `Karpik.Engine.Tooling/`: installation manifests, `global.json` SDK-version parsing, engine-root resolution, atomic directory publication, and launcher/editor handoff records.
- `Karpik.Engine.Tooling.Tests/`: resolver, manifest, hash, and atomic-publication tests.
- `Karpik.Engine.Packager/`: CLI that builds or accepts prepared artifacts and publishes an engine payload.
- `Karpik.Engine.Packager.Tests/`: layout validation and failure-recovery tests.
- `Karpik.Engine.Sdk.IntegrationTests/`: opt-in end-to-end tests that pack the SDK into a local feed, materialize a game below the OS temporary directory, and invoke real `dotnet` subprocesses.
- `templates/Karpik.Game/`: `dotnet new` template containing `global.json`, `.slnx`, and Client/Server/Shared/Test projects that all use `Karpik.Engine.Sdk`.
- `Karpik.Editor/Projects/`: active-project context, MSBuild inspection, opening, switching, and teardown coordination.
- `Karpik.Launcher/` and `Karpik.Launcher.Tests/`: stable launcher UI and orchestration tests.

The public tooling contracts start as:

```csharp
public enum KarpikProjectKind { Runtime, Test, Tool, Generator, Assets }
public enum KarpikProjectSide { Client, Server, Shared, None }

public sealed record KarpikDiagnostic(string Code, string ProjectPath, string Message);
public sealed record KarpikModuleReference(string Id, string? Implementation, bool Optional);
public sealed record KarpikProjectDescriptor(
    string ProjectPath,
    IReadOnlyList<string> SdkNames,
    KarpikProjectKind Kind,
    KarpikProjectSide Side,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<KarpikModuleReference> Modules);

public sealed record KarpikSolutionModel(
    string SolutionPath,
    string SdkVersion,
    IReadOnlyList<KarpikProjectDescriptor> Projects);

public sealed class KarpikSolutionReader
{
    public KarpikSolutionModel Read(string solutionPath);
}

public sealed class KarpikSolutionValidator
{
    public IReadOnlyList<KarpikDiagnostic> Validate(KarpikSolutionModel model);
}
```

The installation and editor contracts start as:

```csharp
public sealed record EngineInstallationManifest(
    string EngineVersion,
    string MsBuildSdkVersion,
    string EditorVersion,
    int RuntimeProtocolVersion,
    int LayoutVersion,
    string ContentHash);

public sealed record EngineInstallation(string RootPath, EngineInstallationManifest Manifest);

public sealed class EngineInstallationResolver
{
    public EngineInstallation Resolve(string sdkVersion, string? overrideRoot = null);
}

public sealed record ProjectRuntimeDescriptor(
    KarpikProjectSide Side,
    string RunnerExecutablePath,
    string GameBundlePath);

public sealed class ActiveProjectContext : IAsyncDisposable
{
    public string SolutionPath { get; }
    public EngineInstallation Installation { get; }
    public IReadOnlyDictionary<KarpikProjectSide, ProjectRuntimeDescriptor> Runtimes { get; }
    public EditorSessionManager Sessions { get; }
    public ValueTask DisposeAsync();
}
```

Names may change only when evidence from a milestone requires it; record the change in the Decision Log before later tasks consume the new name.

## Real-Time Assessment

This work is tooling, build, process orchestration, and editor lifecycle work. Solution parsing, XML/JSON allocation, filesystem enumeration, hashing, MSBuild evaluation, process waits, and locks are acceptable only before runtime startup or during explicit editor operations.

No validator, resolver, manifest check, launcher service, file watcher, or asset-cache scan may be called from `Update`, `FixedUpdate`, ECS `Run`, render loops, serialization loops, or network pumps. Runner argument parsing and bundle selection happen once at process startup. Existing editor snapshots remain bounded and run at their established safe points.

Client projects may reference Client and Shared projects; Server projects may reference Server and Shared projects; Shared projects may reference only Shared projects. `None` is valid for Tool, Generator, and Assets projects and must not gain implicit runtime-side access. Tests declare the side they exercise. Physics/gameplay tick behaviour, ECS data layout, and network delivery semantics are unchanged.

Project switching uses asynchronous cancellation and ordered process teardown, not blocking waits on the Avalonia UI thread. Atomic SDK and bundle publication may use filesystem moves and a short process-local lock because they run outside the game loop. Repeated publication and teardown must be idempotent.

## Plan of Work

Build the new boundary from the inside out. First extract a reusable project model and prove the side and SDK-participation rules independently of MSBuild. Package those rules behind a thin custom SDK and prove a minimal external solution can restore. Build the versioned engine payload and transactional resolver next, because both the game bundle targets and launcher depend on its layout.

Once SDK and payload resolution are stable, materialize the game template outside the repository and make its standard `dotnet` commands pass. Then move bundle ownership from the editor to the game's Client and Server build outputs and teach the engine runner to load an explicit bundle directory. Only after that contract works should the editor gain active-project contexts and safe switching.

Add the launcher after editor command-line opening and installation manifests are proven. Finish by removing `MyGame`, repository launchers, editor-local runtime bundling, and Configurator assumptions from the engine composition. Do not delete the old path until the external fixture covers equivalent server, multi-client, content, module, and hot-reload startup.

## Milestones

### Milestone 1: Reusable solution model and validation

**Files**

- Create `Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj` and focused files under `Model/`, `Parsing/`, and `Validation/`.
- Create `Karpik.Engine.ProjectModel.Tests/Karpik.Engine.ProjectModel.Tests.csproj` and `GameSolutionValidationTests.cs`.
- Modify `Configurator/Models.cs`, `Configurator/RepositoryParser.cs`, and `Configurator/GraphValidator.cs` only where shared side vocabulary can replace duplicate rules without changing current generated artifacts.
- Modify `Configurator.Tests/ConfiguratorTests.cs` to prove existing engine-repository validation is unchanged.
- Modify `KarpikEngine.slnx` to include the new projects.

Write tests first using temporary `.slnx` and `.csproj` files. Cover a valid Runtime Client → Runtime Shared edge; forbidden Client → Server, Server → Client, and Shared → Client/Server edges; missing `Karpik.Engine.Sdk`; missing or invalid kind/side; duplicate and missing solution projects; a project-reference cycle; Test with an explicit side; and Tool/Generator/Assets with `None`.

The reader must inspect raw project XML to prove SDK participation. It must not evaluate untrusted project targets merely to discover whether a project is valid. Normalize paths with `Path.GetFullPath`, compare Windows paths case-insensitively and Unix paths case-sensitively through one injected comparer, and return these stable codes from `Karpik.Engine.ProjectModel/Diagnostics/KarpikDiagnosticCodes.cs`:

- `KARPIK001`: project does not include `Karpik.Engine.Sdk`;
- `KARPIK002`: missing or invalid `KarpikProjectKind`;
- `KARPIK003`: missing or invalid `KarpikSide`;
- `KARPIK004`: solution project is missing, duplicated, unreadable, or outside the allowed solution root;
- `KARPIK005`: forbidden side dependency;
- `KARPIK006`: project-reference cycle;
- `KARPIK007`: unknown or ambiguous module id/implementation;
- `KARPIK008`: required module dependency is disabled or missing.

Validation from `C:\Users\artem\RiderProjects\KarpikEngine`:

    dotnet test Karpik.Engine.ProjectModel.Tests\Karpik.Engine.ProjectModel.Tests.csproj -m:1 -nr:false
    dotnet test Configurator.Tests\Configurator.Tests.csproj -m:1 -nr:false

Expected observation: both projects pass; `dotnet run --project Configurator\Configurator.csproj -- --generate` adds only the two new tooling projects to `Generated/KarpikModuleCatalog.props`; `AutoGenerated.targets` and `Generated/ModuleLoader.cs` remain byte-for-byte unchanged; a subsequent `--validate` passes.

Commit boundary: `feat: add reusable Karpik game project validation`.

### Milestone 2: Pack and resolve `Karpik.Engine.Sdk`

**Files**

- Create `Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj` with `ValidateKarpikSolutionTask.cs` and `ValidateKarpikProjectReferencesTask.cs`.
- Create `Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj` with fake-build-engine tests.
- Create `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`, `Sdk/Sdk.props`, `Sdk/Sdk.targets`, `Sdk/Solution.targets`, `Templates/Directory.Solution.targets`, and `README.md`.
- Add `artifacts/nuget/` to `.gitignore` if the existing ignore rules do not already cover it.
- Add the projects to `KarpikEngine.slnx`.

`Sdk/Sdk.props` imports `Microsoft.NET.Sdk/Sdk/Sdk.props`, defines no implicit side, and requires consumers to set both `KarpikProjectKind` and `KarpikSide`. `Sdk/Sdk.targets` imports `Microsoft.NET.Sdk/Sdk/Sdk.targets`, registers the compiled task assembly, and then defines distinct validation targets before `_GenerateRestoreProjectPathWalk` and `AssignProjectConfiguration`. The task compares normalized evaluated direct `@(ProjectReference)` items with unconditional literal top-level declarations, rejects any mismatch with `KARPIK004`, and validates the complete raw transitive graph. The targets must not shell out to Configurator, evaluate child projects, or load game assemblies.

The external game template also commits the standard `Directory.Solution.targets` file at its solution root. That file imports package-owned `Sdk/Solution.targets` through `Sdk="Karpik.Engine.Sdk"`; the package target validates the raw `.slnx` on the solution metaproject before its `Build` target launches any child project. This solution-scope gate rejects an independent foreign project before its compiler can run and remains version-pinned through the same `global.json` `msbuild-sdks` mapping. Future Milestone 4 project creation copies the packaged template verbatim; no `.karpik` manifest or `Directory.Build.*` contract is introduced.

Pack the local development package as `Karpik.Engine.Sdk` version `0.6.0-local` into `artifacts/nuget`. Create a temporary smoke solution with this `global.json` fragment:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestPatch" },
  "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-local" }
}
```

Use a temporary `NuGet.Config` that adds only the local feed plus the normal configured sources; do not modify the user's global NuGet configuration in tests. Verify a project containing `<Project Sdk="Karpik.Engine.Sdk">` restores and builds; verify an independent foreign project in the `.slnx` fails with `KARPIK001` before compilation in both project-order permutations without artificial `ProjectReference` ordering; verify imported, conditional, and late target-time graph mismatches fail with `KARPIK004`; and verify a cold direct literal cycle fails with `KARPIK006` before NuGet restore graph traversal or compiler diagnostics.

Validation:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet pack Karpik.Engine.Sdk\Karpik.Engine.Sdk.csproj -m:1 -nr:false -p:PackageVersion=0.6.0-local -o artifacts\nuget

Expected observation: the `.nupkg` contains `Sdk/Sdk.props`, `Sdk/Sdk.targets`, `Sdk/Solution.targets`, `templates/Directory.Solution.targets`, and the task/runtime dependency assemblies exactly once; the smoke project builds with plain `dotnet build`.

Commit boundary: `feat: package Karpik custom MSBuild SDK`.

### Milestone 3: Transactional engine payload

**Files**

- Create `Karpik.Engine.Tooling/Karpik.Engine.Tooling.csproj` with `EngineInstallationManifest.cs`, `GlobalJsonSdkVersionReader.cs`, `EngineInstallationResolver.cs`, `EngineInstallationValidator.cs`, and `AtomicDirectoryPublisher.cs`.
- Create `Karpik.Engine.Tooling.Tests/Karpik.Engine.Tooling.Tests.csproj` with resolver and recovery tests.
- Create `Karpik.Engine.Packager/Karpik.Engine.Packager.csproj`, `Program.cs`, `EnginePayloadBuilder.cs`, and `PayloadLayout.cs`.
- Create `Karpik.Engine.Packager.Tests/Karpik.Engine.Packager.Tests.csproj`.
- Add the projects to `KarpikEngine.slnx`.

The manifest JSON fields are exactly `engineVersion`, `msBuildSdkVersion`, `editorVersion`, `runtimeProtocolVersion`, `layoutVersion`, and `contentHash`. The initial `layoutVersion` and `runtimeProtocolVersion` are `1`. The packager accepts explicit `--source`, `--output`, `--engine-version`, and `--sdk-version` arguments, writes to `<output>/.staging/<guid>`, validates all required files, computes a deterministic SHA-256 content hash over sorted normalized relative paths and file bytes, writes `.complete`, and atomically renames the staging directory to `<output>/Engines/<engine-version-or-dev-hash>`.

The payload layout is:

    editor/
    sdk/
    runners/client/
    runners/server/
    modules/
      <module-id>/
        <module-id>.dll
        <module-owned dependencies...>
    native/
    engine-installation.json
    .complete

Tests cover wrong manifest version, a missing runner, a missing completion marker, hash mismatch, interrupted legacy recovery, immutable stable-version conflicts, hash-qualified development payloads, an existing identical destination, bounded subprocess termination, explicit `KarpikEngineRoot`, and default resolution below `%LocalAppData%/Karpik/Engines` (or the platform-equivalent local application-data root).

Validation:

    dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false
    dotnet run --project Karpik.Engine.Packager\Karpik.Engine.Packager.csproj -- --source . --output artifacts\karpik-home --engine-version 0.6.0-dev --sdk-version 0.6.0-local

Expected observation: only complete, hash-valid payloads appear below `artifacts/karpik-home/Engines`; rerunning identical content reuses the same installation, differing development content publishes beside it under a full-hash-qualified name, a valid stable-version destination is never moved or replaced, and each module retains its complete clean build output in an isolated subtree.

Commit boundary: `feat: add transactional engine SDK payload packaging`.

### Milestone 4: External game template and ordinary `dotnet` workflow

**Files**

- Create `templates/Karpik.Game/.template.config/template.json`.
- Create template `global.json`, `KarpikGame.slnx`, and projects under `Source/KarpikGame.Client`, `Source/KarpikGame.Server`, `Source/KarpikGame.Shared`, and `Tests/KarpikGame.Tests`.
- Create `Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj` and `ExternalGameCliTests.cs`.
- Add only the integration-test project, not the generated game, to `KarpikEngine.slnx`.

Every template `.csproj` uses `Karpik.Engine.Sdk`. Runtime projects declare `KarpikProjectKind=Runtime` and Client/Server/Shared respectively. Tests declare `KarpikProjectKind=Test` and the side they exercise. The template contains no `.karpik`, `Directory.Build.props`, `Directory.Build.targets`, or relative reference to KarpikEngine source.

The opt-in integration test creates a unique directory below `Path.GetTempPath()`, verifies that directory is not below the repository root, installs the template there, points NuGet at `artifacts/nuget`, sets `KarpikEngineRoot` to the payload from Milestone 3, and runs real subprocesses with bounded timeouts and captured output:

    dotnet restore KarpikGame.slnx
    dotnet build KarpikGame.slnx -m:1 -nr:false --no-restore
    dotnet test KarpikGame.slnx -m:1 -nr:false --no-build
    dotnet publish Source\KarpikGame.Client\KarpikGame.Client.csproj -m:1 -nr:false --no-restore

It then mutates a copy so one project uses `Microsoft.NET.Sdk` and proves solution build fails with `KARPIK001`. A second mutation creates a Client → Server reference and proves failure with the side-boundary diagnostic.

Validation:

    $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION='1'
    dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false
    Remove-Item Env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION

Expected observation: all four standard `dotnet` operations pass outside the repository and both invalid copies fail before runtime startup.

Commit boundary: `feat: add external Karpik game template`.

### Milestone 5: Game-owned bundles and engine-owned runners

**Files**

- Add `BuildKarpikRuntimeBundleTask.cs` and tests to `Karpik.Engine.Sdk.Tasks/` and `.Tests/`.
- Modify `Karpik.Engine.Sdk/Sdk/Sdk.props` and `Sdk/Sdk.targets` to expose `KarpikRuntimeBundlePath` and create bundles only for Runtime Client/Server projects.
- Create `Karpik.Engine.Core/ProcessManagement/RuntimeLaunchOptions.cs`.
- Modify `Karpik.Engine.Core/Editor/EditorPreviewController.cs` and `Karpik.Engine.Core/ProcessManagement/ProcessManager.cs` to accept runner executable and bundle working directory separately.
- Modify `Karpik.Engine.Core.Runner/Program.cs` and `Runner.cs` to require an explicit `--bundle <absolute-path>` argument and load modules/content from that directory.
- Extend `Karpik.Engine.Core.Runner.Tests/` with runner-argument and explicit-bundle tests.
- Extend `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs` with side-purity and process-start coverage.

For each Runtime Client/Server project, default `KarpikRuntimeBundlePath` to `$(TargetDir)karpik-bundle/`. Publish through a sibling staging directory, verify the side marker and completed module staging marker, then atomically replace the final bundle. Never copy the engine runner into the game bundle. The runner executable comes from the resolved engine installation and receives the game bundle as an explicit argument.

Tests prove the Client bundle has no Server assemblies, the Server bundle has no Client graphics/input/window assemblies, both include Shared game assemblies and required content, and a runner cannot accidentally fall back to `AppContext.BaseDirectory` when the bundle argument is missing.

Validation:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION='1'
    dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --filter RuntimeBundle
    Remove-Item Env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION

Expected observation: the engine runner starts an external game's bundle without any repository-relative path and the bundle trees are side-pure.

Commit boundary: `feat: move runtime bundle ownership to game builds`.

### Milestone 6: One active external project in the editor

**Files**

- Create `Karpik.Editor/Projects/ProjectOpenResult.cs`, `MsBuildProjectInspector.cs`, `ActiveProjectContext.cs`, `ProjectOpenService.cs`, and `ProjectSwitchCoordinator.cs`.
- Create matching tests under `Karpik.Editor.Tests/Projects/`.
- Replace `Karpik.Editor/Runtime/RuntimeBundleResolver.cs` with `ProjectRuntimeResolver.cs` and update its tests.
- Modify `Karpik.Editor/Runtime/EditorPreviewBackendFactory.cs` to consume `ProjectRuntimeDescriptor` instances.
- Modify `Karpik.Editor/ViewModels/EditorShellViewModel.cs`, `Models/EditorWorkspace.cs`, `MainWindow.axaml`, and `MainWindow.axaml.cs` for asynchronous `.slnx` open/switch and workspace persistence.
- Modify `Karpik.Editor/Program.cs` to accept `--solution <absolute-path>` and `--handoff <absolute-path>`.
- Remove the `EditorRuntimeBundles.targets` import from `Karpik.Editor/Karpik.Editor.csproj` only after the new tests and external runtime smoke pass.

`ProjectOpenService.OpenAsync` first performs raw safe validation, then invokes MSBuild in a child process for evaluated properties and items with a bounded timeout. It returns a candidate context without publishing it. `ProjectSwitchCoordinator.SwitchAsync` blocks new commands, cancels any active build, stops clients, stops the server, disposes IPC/watchers/services, saves the old workspace, disposes the old context, validates the candidate, and finally publishes it as active. Failure before publication leaves no active context.

Tests use fake contexts and backends to prove exact teardown order, no overlapping active contexts, cancellation, failed teardown preventing open, failed candidate leaving no active project, workspace path round-trip as `.slnx`, and stale snapshot/output rejection after switching. A real opt-in test switches between two temporary external games and verifies worker PIDs exit and bundle paths change.

Validation:

    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false
    dotnet build Karpik.Editor\Karpik.Editor.csproj -m:1 -nr:false
    $env:KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION='1'
    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --filter ExternalProjectSwitch
    Remove-Item Env:KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION

Expected observation: the editor can open either external game, run one server and multiple clients from that game's bundles, switch projects, and leave no process, IPC endpoint, watcher, or locked file from the old context.

Commit boundary: `feat: add transactional external project switching to editor`.

### Milestone 7: Stable launcher and version-matched editors

**Files**

- Create `Karpik.Launcher/Karpik.Launcher.csproj`, Avalonia app/window files, `Models/RecentProject.cs`, `Services/ProjectRegistry.cs`, `Services/EditorResolver.cs`, `Services/EditorProcessHost.cs`, and `ViewModels/LauncherViewModel.cs`.
- Create `Karpik.Launcher.Tests/Karpik.Launcher.Tests.csproj` with registry, resolution, and process-handoff tests.
- Add `EditorHandoffRequest.cs` and `EditorExitCodes.cs` to `Karpik.Engine.Tooling/`.
- Modify `Karpik.Editor/Projects/ProjectSwitchCoordinator.cs` to write a handoff request and return the dedicated exit code when the target project requires an incompatible editor.
- Modify `Karpik.Engine.Packager/` so every payload includes its compatible editor.
- Add launcher projects to `KarpikEngine.slnx`.

The launcher reads only `global.json` and installation manifests before selecting an editor. It stores recent `.slnx` paths below the platform local application-data directory, never inside a game. It starts the editor with a unique handoff file path and waits asynchronously. Exit code `20` means the editor wrote a validated handoff request for another solution; the launcher resolves the new version and loops. Other non-zero codes are surfaced as failures and do not trigger automatic retries.

Tests create two fake installations with distinct editor executables and manifests. They prove exact-version selection, missing/corrupt/incomplete installation errors, recent-project deduplication, normal exit, exit-code-20 handoff, malformed handoff rejection, and bounded restart loops.

Validation:

    dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false
    dotnet build Karpik.Launcher\Karpik.Launcher.csproj -m:1 -nr:false

Manual smoke: install two development payloads with different compatible editor versions, open one project through the launcher, request a switch to the second project, observe all old workers exit, and observe the launcher start the second editor with the requested `.slnx`.

Commit boundary: `feat: add version-aware Karpik project launcher`.

### Milestone 8: Remove monorepository game assumptions

Milestone 8 is delivered through six ordered stop points. Complete and validate one slice at a time; each slice may be committed independently. Slices 8C through 8E are destructive migration work and must not begin until 8B proves that the external runtime path replaces the legacy composition roots.

#### Milestone 8A: Compose installed engine modules with game bundles

- Extend the explicit runtime composition so a version-matched runner loads the side-compatible engine module set from the validated installed payload and the Client/Server+Shared assemblies from the game-owned bundle.
- Keep engine runtime infrastructure such as `Karpik.Engine.Core`, `Karpik.Engine.Core.Runner`, `Dragon`, and `Karpik.Jobs` identity-shared. Do not directly load a second copy into the collectible plugin context.
- Register engine installers and game installers through one deterministic module-registration pass while preserving Client/Server/Shared boundaries.
- Add focused loader and runtime tests that fail if a shared assembly is duplicated, if `ECSInstaller` is absent, or if a server can reach a client-only module.

Exit criteria: an externally built Server bundle starts through the installed runner, `ECSInstaller` registers `EcsDefaultWorld`, and an editor snapshot request returns normally without `Not found service DCFApixels.DragonECS.EcsDefaultWorld`.

#### Milestone 8B: Prove the complete external runtime replacement

- Extend the external fixture from the current empty-state runtime proof to a real ECS sample with representative shared content.
- Start one server and two clients outside the engine repository, verify side-pure module composition, request an editor snapshot, and perform hot reload with non-empty ECS/module state.
- Stop and switch the project, then prove that worker processes, IPC endpoints, shadow copies, state files, and file locks are released.

Exit criteria: the server-plus-two-clients smoke passes using only a local SDK feed, a validated versioned engine payload, and game-owned bundles. Record this evidence in `Progress` before deleting any legacy composition path.

#### Milestone 8C: Remove editor-local runtime packaging

- [x] Removed `Karpik.Editor/EditorRuntimeBundles.targets` (was already a dead file — not imported by any build script).
- [x] Removed `-p:SkipEditorRuntimeBundles=true` from `Karpik.Engine.Packager/PayloadLayout.cs:88`.
- [x] Removed `AdditionalProperties="SkipEditorRuntimeBundles=true"` from `Karpik.Editor.Tests/Karpik.Editor.Tests.csproj:17`.
- [x] Renamed `RuntimeBundleResolverTests.cs` → `ProjectRuntimeResolverTests.cs` to reflect current implementation.

Exit criteria: editor build, editor tests (87/87 pass, 1 skip=opt-in, 1 pre-existing unrelated handoff failure), and the external project-switch smoke pass without producing or consuming editor-local runtime bundles.

#### Milestone 8D: Remove the repository-local game composition roots

- Move reusable `MyGame` sample behaviour, systems, and content into `templates/Karpik.Game/` where appropriate.
- Remove `MyGame/`, `ClientLauncher/`, `ServerLauncher/`, their solution entries, and their game/resource references from shared build files and generated artifacts.

Exit criteria: the external template retains equivalent sample behaviour while the engine solution and build graph contain no repository-local game or game launcher.

For the accepted remediation, "equivalent sample behaviour" means the already-approved external acceptance sample: a side-pure server plus two clients, a non-empty Dragon ECS world with state surviving hot reload, bundle-owned `Content/` and `Mods/`, readable representative assets, and a real editor project switch. It does not mean restoring the deleted platformer implementation. The old network/physics/rendering sample remains available in Git history and may be redesigned as a separate external sample after `Network.Codegen` no longer assumes `MyGame.*.Main` assembly names and namespaces.

##### Approved Milestone 8B-8D remediation

###### Task 1 (R1): Restore the portable game-bundle asset contract

- Tests first: extend `Karpik.Engine.Sdk.Tasks.Tests/BuildKarpikRuntimeBundleTaskTests.cs` so a bundle containing `Content/runtime.txt` and `Mods/MyCoolMod/mod_info.json` must publish both roots, and so an escaping, linked, duplicate, or oversized mod item fails transactionally without replacing the prior complete bundle.
- Modify `Karpik.Engine.Sdk.Tasks/BuildKarpikRuntimeBundleTask.cs` to accept a separate `Mods` item array, materialize it below bundle-owned `Mods/`, include it in the existing entry/depth/byte bounds, and permit that root in complete-bundle validation. `Content` remains required; `Mods` is optional.
- Modify `Karpik.Engine.Sdk/Sdk/Sdk.targets` to collect `$(TargetDir)Mods/**` after build, pass stable relative `TargetPath` metadata to the task, and keep Shared-project content flowing transitively through the side output.
- Restore `templates/Karpik.Game/Source/KarpikGame.Client/Content/runtime.txt`, `templates/Karpik.Game/Source/KarpikGame.Server/Content/runtime.txt`, and `templates/Karpik.Game/Source/KarpikGame.Shared/Content/shared-runtime.txt`. Restore the corresponding startup reads in `ClientGameInstaller.cs` and `ServerGameInstaller.cs`; these are cold initialization paths, not frame-loop work.
- Extend `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs` so template structure, side bundles, server/client log output, and moved Lua mod files are asserted from the bundle. The test must continue to prove `GameComponent(42)` and exact `TotalEntityCount == before + 1` after hot reload.
- Remove the current uncommitted `ContentRoot`/`KARPIK_CONTENT_ROOT` changes from `ProjectOpenResult.cs`, `ProjectOpenService.cs`, `ProjectRuntimeResolver.cs`, `EditorPreviewBackendFactory.cs`, `RuntimeLaunchOptions.cs`, `ProcessManager.cs`, and `AssetsManager.cs`; no source-tree fallback remains.

Run from the repository worktree:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false --no-restore
    dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --no-restore

Expected observation: all ordinary tests pass; generated side bundles contain `Content/` and `Mods/`; no runtime launch option or environment variable can bypass the bundle.

###### Task 2 (R2): Repair Milestone 8C runtime ownership acceptance

- Rewrite `Karpik.Editor.Tests/PreviewIntegrationTests.cs` so opt-in runtime tests take a validated installation from `KARPIK_TEST_ENGINE_ROOT` and game bundles from `KARPIK_TEST_GAME_ROOT`. Use `Assert.SkipUnless` when the opt-in environment is absent; never silently return and never derive runners or bundles from `Karpik.Editor/bin/.../runtimes`.
- Rewrite the setup in `Karpik.Editor.Tests/ProjectRuntimeResolverTests.cs` with physically separate `engine/runners/{side}` and `game/.../karpik-bundle` roots. Assert that the descriptor returns the exact installed runner and active-game bundle for each side.
- Add a source/build-contract test in `Karpik.Editor.Tests` that loads `Karpik.Editor/Karpik.Editor.csproj` and asserts there is no `EditorRuntimeBundles.targets` import, `SkipEditorRuntimeBundles`, or target that creates `$(TargetDir)runtimes`.

Run:

    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ProjectRuntimeResolverTests|FullyQualifiedName~PreviewIntegrationTests|FullyQualifiedName~EditorProject"

Expected observation: ordinary ownership tests pass, opt-in tests are reported as skipped without their environment, and every constructed runner/bundle pair crosses the installation/game boundary explicitly.

###### Task 3 (R3): Replace the false-positive switch claim with coordinator evidence

- Extend `Karpik.Editor.Tests/Projects/ExternalProjectSwitchIntegrationTests.cs`: start one server and two clients for the first generated game, keep all three PIDs, call `EditorShellViewModel.OpenProjectAsync` for the second solution while they are active, and assert every old PID exits before the new project becomes active.
- After the switch, assert the first client and server bundle `reload/state` and `reload/shadow` trees are empty, and acquire exclusive read/write handles on representative first-project bundle files to prove locks were released.
- Start one server and two clients for the second game, assert their runtime descriptors are below the second game and their runners are below the selected installation, then call `ShutdownAsync` and repeat PID, reload-tree, and exclusive-handle checks.
- Keep `ExternalGameCliTests` responsible for the full standalone ECS/content/hot-reload cycle on two independently generated games, but change its wording and Progress evidence so it no longer claims that sequential execution itself is an editor switch.

Run after recreating the local package and engine payload described in `Concrete Steps`:

    $env:KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION='1'
    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ExternalProjectSwitch_StopsOldWorkersAndUsesTheNewGameBundles"

Expected observation: one opt-in test passes with three first-project workers and three second-project workers; no old process, reload artifact, or locked representative file survives the switch or shutdown.

###### Task 4 (R4): Make the destructive migration internally consistent

- Remove `RepositoryModel.GameRoots` and its remaining validation loop from `Configurator/Models.cs` and `Configurator/GraphValidator.cs`. Remove the four deleted `MyGame` fixture projects and helpers from `Configurator.Tests/ConfiguratorTests.cs`; replace them with assertions over an engine-only repository model.
- Replace the deleted-launcher diagnostic in `Configurator/ArtifactGenerator.cs` with guidance to build the selected first-party module set or use the installed side runner. Run Configurator generation so `Generated/ModuleLoader.cs` changes only through the generator.
- Add tests asserting generated artifacts contain none of `MyGame`, `ClientLauncher`, or `ServerLauncher`. Keep historical prose cleanup for Milestone 8F, but production projects, evaluated build inputs, generator output, and active acceptance tests must not require those names.
- Reconcile `Progress`, the kanban board, and `docs/02_ADR/editor-desktop-stack.md` only after R1-R3 pass. Mark 8D complete only after the external template and actual switch smoke both pass.

Run:

    dotnet run --project Configurator\Configurator.csproj --no-restore -- --generate
    dotnet run --project Configurator\Configurator.csproj --no-restore -- --validate
    dotnet test Configurator.Tests\Configurator.Tests.csproj -m:1 -nr:false --no-restore
    rg -n "MyGame|ClientLauncher|ServerLauncher" AutoGenerated.targets Generated Configurator Karpik.Editor Karpik.Engine.Client.Publish Karpik.Engine.Server.Publish KarpikEngine.slnx

Expected observation: Configurator validation and tests pass, generated artifacts contain no deleted game/launcher names, and any remaining matches are either historical documentation or explicitly recorded future `Network.Codegen` modernization work.

Real-time assessment for R1-R4: bundle creation, project opening, process startup, installer configuration, Configurator, and tests are cold paths. No change enters `Update`, `FixedUpdate`, ECS `Run`, rendering, serialization loops, or network pumps. The only runtime lookup remains `AssetsManager.RootPath` against the worker base directory; no new per-frame allocation, lock, pointer chasing, or cross-side reference is introduced.

#### Milestone 8E: Decouple Configurator from the game profile

- Remove `MyGame` roots and root game-profile assumptions from `Configurator/RepositoryParser.cs`, `Models.cs`, `GraphValidator.cs`, and `ArtifactGenerator.cs`.
- Preserve validation and generation for the engine's first-party module graph and emit the SDK payload module catalog needed by 8A.
- Regenerate `Generated/KarpikModuleCatalog.props` and `Generated/ModuleLoader.cs` through Configurator, never by hand.

Exit criteria: Configurator generation and validation are deterministic, module boundary tests pass, and no generated artifact mentions `MyGame`, `ClientLauncher`, or `ServerLauncher`.

#### Milestone 8F: Final acceptance and documentation

- Run the complete automatic validation set below, both opt-in external suites, and the manual two-SDK launcher/editor smoke.
- Update `README.md`, `README-ENG.md`, the accepted ADR, this ExecPlan, the kanban board, and the Graphify cache.
- Inspect the final diff for game-specific paths and confirm no new work entered real-time hot paths.

Exit criteria: every acceptance item in this plan passes, the engine and external game build independently, and Milestone 8 plus the parent ExecPlan can be marked complete.

**Files**

- Remove `Karpik.Editor/EditorRuntimeBundles.targets` after all references are gone.
- Remove `ClientLauncher/`, `ServerLauncher/`, and their solution entries after runner and bundle integration tests replace their composition-root role.
- Move reusable `MyGame` sample source/content into `templates/Karpik.Game/` where appropriate, then remove `MyGame/` and its solution entries.
- Remove `MyGame` and launcher entries from `Plugins.targets`, `Directory.Build.props`, `Directory.Build.targets`, `AutoGenerated.targets`, and generated catalog/loader output.
- Modify `Configurator/RepositoryParser.cs`, `Models.cs`, `GraphValidator.cs`, and `ArtifactGenerator.cs` so engine repository validation has no `MyGame` roots and emits an SDK payload module catalog instead of a game-specific graph.
- Update `Generated/KarpikModuleCatalog.props` and `Generated/ModuleLoader.cs` through Configurator, not by hand.
- Update `README.md`, `README-ENG.md`, `docs/02_ADR/editor-desktop-stack.md`, this ExecPlan, and the kanban board.

Before deleting old paths, prove the external template carries the required sample behaviour and content and that the current server-plus-two-clients smoke succeeds outside the repository. Keep first-party modules under `Modules/`; the separation requirement concerns games, game launchers, and game resources, not the engine's own module source.

Validation from the repository root:

    dotnet run --project Configurator\Configurator.csproj -- --generate
    dotnet run --project Configurator\Configurator.csproj -- --validate
    dotnet test Configurator.Tests\Configurator.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.ProjectModel.Tests\Karpik.Engine.ProjectModel.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false
    dotnet build KarpikEngine.slnx -m:1 -nr:false --no-restore
    git diff --check
    graphify update .

Then run both opt-in external SDK and editor switching suites and the manual cross-version launcher smoke described above.

Expected observation: no evaluated build, generated artifact, editor path, or runtime startup requires `MyGame`, `ClientLauncher`, `ServerLauncher`, or a game below the engine repository root. The engine solution, external game solution, and two installed payloads remain independently buildable.

Commit boundary: `refactor: separate engine source from game projects`.

## Concrete Steps

Work milestone by milestone. Before each milestone, update `Progress` with the intended start and verify `git status --short` so unrelated `.obsidian` changes remain untouched. Within a milestone, use the sequence: add the smallest failing unit/integration test, run it and record the expected failure, add the minimal production code, rerun the targeted test, run the milestone validation set, update this plan, and commit only the milestone files.

Use single-node .NET commands throughout:

    dotnet build <project-or-solution> -m:1 -nr:false
    dotnet test <project-or-solution> -m:1 -nr:false

When a real subprocess test needs a local SDK package, first recreate the local feed deterministically:

    dotnet pack Karpik.Engine.Sdk\Karpik.Engine.Sdk.csproj -m:1 -nr:false -p:PackageVersion=0.6.0-local -o artifacts\nuget

When a test needs an engine payload, publish it below repository-local ignored artifacts, not into the user's real installation store:

    dotnet run --project Karpik.Engine.Packager\Karpik.Engine.Packager.csproj -- --source . --output artifacts\karpik-home --engine-version 0.6.0-dev --sdk-version 0.6.0-local

Do not run deletion migrations in Milestone 8 until the Milestone 4 through 7 acceptance evidence is recorded in `Progress`.

## Validation and Acceptance

Automatic acceptance requires all milestone test projects and the engine solution build to pass with single-node MSBuild. The external integration test must create its game outside the repository and demonstrate successful restore, build, test, publish, client bundle construction, server bundle construction, and runtime start using only the local NuGet SDK feed and versioned engine payload.

Invalid solutions must fail with stable diagnostics when any project omits `Karpik.Engine.Sdk`, omits kind/side, violates side boundaries, contains a cycle, selects an unknown/disabled module, or references a missing project. Direct project builds must cover their full transitive project-reference graph.

User-visible acceptance requires:

- Launcher shows recent `.slnx` projects and selects the exact installed SDK/editor version from `global.json`.
- A game opens without a `.karpik` manifest or required `Directory.Build.props`.
- The same game builds through editor commands and ordinary terminal `dotnet` commands.
- A source-built payload override builds and runs the game without changing committed game files.
- One editor owns one active project, one server, and multiple clients.
- Same-version switching happens in-process after complete teardown; incompatible switching returns through the launcher and starts the matching editor.
- Failed SDK resolution, validation, build, bundle publication, runtime startup, or project switching leaves an explicit diagnostic and no partially active project.
- After migration, the engine repository contains no game-specific composition root or resource path.

Real-time acceptance requires code inspection and tests to confirm that all new work stops at build/startup/editor boundaries. There must be no new invocation from `Update`, `FixedUpdate`, ECS `Run`, render, serialization, or network pump paths.

## Idempotence and Recovery

SDK packing, payload publication, bundle publication, template materialization, project open, project teardown, and launcher handoff must be safe to retry. Staging directories include unique names and become visible only after validation and a completion marker. Engine payloads are immutable: valid stable-version collisions fail without moving the destination, while differing development content uses a hash-qualified name. Later mutable bundle publication may use replacement recovery, but it must restore the last complete destination on failure. A packager subprocess whose exit cannot be confirmed leaves marked owned staging for inspection and recovery must not delete it.

Tests use unique temporary directories and kill owned process trees in `finally` blocks. They never modify global NuGet sources or the real `%LocalAppData%/Karpik/Engines` store. A failed editor switch disposes the candidate and leaves no active project. A failed cross-version handoff does not restart indefinitely; the launcher shows the error and returns to project selection.

Milestones 1 through 7 are additive and can be rolled back by reverting their milestone commits. Milestone 8 deletes legacy paths only after replacement coverage passes. If Milestone 8 fails, revert only its migration commit and keep the additive SDK/payload/editor infrastructure; do not use `git reset --hard` and do not touch unrelated `.obsidian` files.

## Artifacts and Notes

- Accepted architecture: `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`.
- Desktop editor decision: `docs/02_ADR/editor-desktop-stack.md`.
- Existing module graph decision: `docs/02_ADR/module-graph.md`.
- Delivery board: `docs/04_Roadmap/kanban-versioned-sdk-external-projects.md`.
- Existing editor baseline: `plans/editor-first-slice-execplan.md`.
- Existing multisession baseline: `plans/editor-multisession-launch-execplan.md`.
- Local, ignored package feed: `artifacts/nuget/`.
- Local, ignored payload store: `artifacts/karpik-home/`.
