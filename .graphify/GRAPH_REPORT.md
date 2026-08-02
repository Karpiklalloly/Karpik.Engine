# Graph Report - .  (2026-08-01)

## Corpus Check
- Large corpus: 711 files · ~264 569 words. Semantic extraction will be expensive (many Claude tokens). Consider running on a subfolder, or use --no-semantic to run AST-only.

## Summary
- 425 nodes · 1820 edges · 38 communities detected
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output
- Edge kinds: ON_BRANCH: 1000 · PARENT_OF: 290 · MODIFIES: 254 · imports: 150 · references: 126


## Input Scope
- Requested: auto
- Resolved: committed (source: default-auto)
- Included files: 711 · Candidates: 2147
- Excluded: 20 untracked · 65474 ignored · 0 sensitive · 9 missing committed
- Recommendation: Use --scope all or graphify.yaml inputs.corpus for a knowledge-base folder.

## Graph Freshness
- Built from Git commit: `ccde2d7`
- Compare this hash to `git rev-parse HEAD` before trusting freshness-sensitive graph output.
## God Nodes (most connected - your core abstractions)
1. `net10.0` - 59 edges
2. `Microsoft.NET.Sdk` - 57 edges
3. `Microsoft.NET.Test.Sdk` - 14 edges
4. `xunit.runner.visualstudio` - 14 edges
5. `xunit` - 9 edges
6. `Karpik.Engine.Sdk` - 6 edges
7. `xunit.v3` - 5 edges
8. `Microsoft.CodeAnalysis.CSharp` - 5 edges
9. `Newtonsoft.Json` - 4 edges
10. `netstandard2.0` - 4 edges

## Surprising Connections (you probably didn't know these)
- `014040c texture` --ON_BRANCH--> `codex/threaded-client-runtime`  [EXTRACTED]
  git → git  _Bridges community 6 → community 4_
- `014040c texture` --ON_BRANCH--> `main`  [EXTRACTED]
  git → git  _Bridges community 6 → community 2_
- `014040c texture` --ON_BRANCH--> `v-0.5`  [EXTRACTED]
  git → git  _Bridges community 6 → community 3_
- `02775e2 Add more consistent behaviour for ModContainer` --ON_BRANCH--> `codex/threaded-client-runtime`  [EXTRACTED]
  git → git  _Bridges community 8 → community 4_
- `02775e2 Add more consistent behaviour for ModContainer` --ON_BRANCH--> `main`  [EXTRACTED]
  git → git  _Bridges community 8 → community 2_

## Communities

### Community 0 - "Community 0"
Cohesion: 0.07
Nodes (49): Dragon.csproj, Karpik.Jobs.csproj, @(KarpikModuleDependency->'%(ResolvedProjectPath)'), DragonECS.csproj, 27c8586 Merge branch 'v-0.5', 37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, 40f1a5f Merge pull request #3 from Karpiklalloly/v-0.3 (+41 more)

### Community 1 - "Community 1"
Cohesion: 0.06
Nodes (99): codex/versioned-sdk-external-projects, v-0.6, 00be65c fix: harden versioned SDK review findings, 00d8066 fix: harden editor runtime ownership, 01729bf Add sdk local publish script, 05c206f refactor: complete engine-only configurator migration, 0c32359 update plan, 127441d fix: isolate worker lifecycle generations (+91 more)

### Community 2 - "Community 2"
Cohesion: 0.11
Nodes (33): main, 173769b Test? 2, 186d6fc 2, 1ba4823 Test? 3, 1ddaca8 2, 2260a53 atlas support, 2bdfa0b 1, 2db1499 2 (+25 more)

### Community 3 - "Community 3"
Cohesion: 0.12
Nodes (33): v-0.5, 1041835 1, 1aa1aec 8, 1b730d4 ValueJobHandle, 1e925ae Benchmarks, 3207859 delete, 3370bd0 input, 3447c65 2 (+25 more)

### Community 4 - "Community 4"
Cohesion: 0.16
Nodes (24): codex/threaded-client-runtime, 144827d yes, 1e3efd4 plan done!, 1e567c1 1, 1ec4d08 cleanup, 3540abe plan, 3d79774 Make demomodule great again, 4e7805f 2 (+16 more)

### Community 5 - "Community 5"
Cohesion: 0.11
Nodes (20): 260eafa Camera component, 2cba8e9 Physics scene, 2e02b84 1, 2e6ff63 Fix scene, 30e1bb2 Fix stupid bug in Drawe :), 3134331 Fix boxes, 363ae71 1, 37693d6 update todo (+12 more)

### Community 6 - "Community 6"
Cohesion: 0.16
Nodes (14): 014040c texture, 0d242cb Not full implementation, 60246eb fix resize, 603320b a bit refactoring, 6288bfb 1, 67f252e batching, 765359c 1, b0afcff improve (+6 more)

### Community 7 - "Community 7"
Cohesion: 0.31
Nodes (7): 20fbcfb feat: add external Karpik game template, 31ffd9f fix: isolate external SDK integration processes, 3578d3e feat: move runtime bundle ownership to game builds, 86abdce fix: enforce isolated module payload contract, a420207 refactor: migrate MyGame sample to template, remove engine-root launchers, aeabe44 fix: restore portable runtime bundle assets, Karpik.Engine.Sdk

### Community 8 - "Community 8"
Cohesion: 0.22
Nodes (9): 02775e2 Add more consistent behaviour for ModContainer, 25ba870 Add IsEnabled check for ModContainer, 4fdc56e 1, 56e7fce Add World, 5c1e40f Clean, 7f9c682 Get filename once at LoadRootScripts in ModContainer, 8b6f312 Add IServiceContainer as parameter of OnPrepareHotReload, b676b3a Fix FirstPeer throws exception if there is no server connection (+1 more)

### Community 9 - "Community 9"
Cohesion: 0.25
Nodes (8): 1342a16 A bit of physics plans, 2e6efe3 fix hot reload building, 523ee03 Update Readme, 59e869e fix, 94d1cd8 Update Readme, b87ce37 Update configurator, c6d7dfd Make server greate again (compilatable), c982726 Move launching runner into Karpik.Engine.Core

### Community 10 - "Community 10"
Cohesion: 0.22
Nodes (9): 293aa74 controller beta 5, 2aa24a0 controller beta, 5f0a59c server hot reload, 6e9d00e controller beta 3, 7e3ab34 hot reload reconnection, 7eff6ae clean world, 9d57ed8 controller beta 4, d35f696 Rename (+1 more)

### Community 11 - "Community 11"
Cohesion: 0.25
Nodes (7): Veldrid.SPIRV.csproj, Veldrid.ImGui.csproj, Veldrid.OpenGLBindings.csproj, Veldrid.SDL2.csproj, Veldrid.StartupUtilities.csproj, Veldrid.Utilities.csproj, Veldrid.csproj

### Community 12 - "Community 12"
Cohesion: 0.25
Nodes (8): 26bf94e Add no aspect check, 5cd4174 fix read->write confilct, 64fa65e lifecycle tests, 73043e4 update plan, afe71bd Tests, c53c4bc fix destroy, d4f6c50 no gc, f7eac8f update docs

### Community 13 - "Community 13"
Cohesion: 0.33
Nodes (6): 06c795c build update, 0e99ede Implement to SpriteRenderer, 2fabb7c World expansion, 54ca817 Analyzer, 67a9ccf log, 91c7738 clean 2

### Community 14 - "Community 14"
Cohesion: 0.33
Nodes (6): 10f4448 update, 201b73e mb fix crash on hot reload sometimes, 2256d14 a bit of ai analysis, 2a2fa87 omg, 6b96307 Debugger attachment, 8b8acbf test

### Community 15 - "Community 15"
Cohesion: 1.00
Nodes (1): Configurator.csproj

### Community 16 - "Community 16"
Cohesion: 1.00
Nodes (1): DragonExtensions.csproj

### Community 17 - "Community 17"
Cohesion: 1.00
Nodes (1): Karpik.Editor.csproj

### Community 18 - "Community 18"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Core.Codegen.csproj

### Community 19 - "Community 19"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Core.csproj

### Community 20 - "Community 20"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Core.Runner.csproj

### Community 21 - "Community 21"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Core.Runner.TestWorker.csproj

### Community 22 - "Community 22"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Packager.csproj

### Community 23 - "Community 23"
Cohesion: 1.00
Nodes (1): Karpik.Engine.ProjectModel.csproj

### Community 24 - "Community 24"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Sdk.csproj

### Community 25 - "Community 25"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Sdk.Tasks.csproj

### Community 26 - "Community 26"
Cohesion: 1.00
Nodes (1): Karpik.Engine.Tooling.csproj

### Community 27 - "Community 27"
Cohesion: 1.00
Nodes (1): Karpik.Launcher.csproj

### Community 28 - "Community 28"
Cohesion: 1.00
Nodes (1): Graphics.Core.csproj

### Community 29 - "Community 29"
Cohesion: 1.00
Nodes (1): Input.csproj

### Community 30 - "Community 30"
Cohesion: 1.00
Nodes (1): Window.Core.csproj

### Community 31 - "Community 31"
Cohesion: 1.00
Nodes (1): Window.Headless.csproj

### Community 32 - "Community 32"
Cohesion: 1.00
Nodes (1): AssetManagement.Core.csproj

### Community 33 - "Community 33"
Cohesion: 1.00
Nodes (1): ECS.Core.csproj

### Community 34 - "Community 34"
Cohesion: 1.00
Nodes (1): KarpikGame.Client.csproj

### Community 35 - "Community 35"
Cohesion: 1.00
Nodes (1): KarpikGame.Server.csproj

### Community 36 - "Community 36"
Cohesion: 1.00
Nodes (1): KarpikGame.Shared.csproj

### Community 37 - "Community 37"
Cohesion: 1.00
Nodes (1): StaticAnalyzer.csproj

## Knowledge Gaps
- **41 isolated node(s):** `Configurator.csproj`, `@(KarpikModuleDependency->'%(ResolvedProjectPath)')`, `Karpik.Engine.Core.csproj`, `DragonECS.csproj`, `ECS.Core.csproj` (+36 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 15`** (1 nodes): `Configurator.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (1 nodes): `DragonExtensions.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 17`** (1 nodes): `Karpik.Editor.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 18`** (1 nodes): `Karpik.Engine.Core.Codegen.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 19`** (1 nodes): `Karpik.Engine.Core.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 20`** (1 nodes): `Karpik.Engine.Core.Runner.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 21`** (1 nodes): `Karpik.Engine.Core.Runner.TestWorker.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 22`** (1 nodes): `Karpik.Engine.Packager.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 23`** (1 nodes): `Karpik.Engine.ProjectModel.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 24`** (1 nodes): `Karpik.Engine.Sdk.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 25`** (1 nodes): `Karpik.Engine.Sdk.Tasks.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 26`** (1 nodes): `Karpik.Engine.Tooling.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 27`** (1 nodes): `Karpik.Launcher.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 28`** (1 nodes): `Graphics.Core.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 29`** (1 nodes): `Input.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 30`** (1 nodes): `Window.Core.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 31`** (1 nodes): `Window.Headless.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 32`** (1 nodes): `AssetManagement.Core.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 33`** (1 nodes): `ECS.Core.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 34`** (1 nodes): `KarpikGame.Client.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 35`** (1 nodes): `KarpikGame.Server.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 36`** (1 nodes): `KarpikGame.Shared.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 37`** (1 nodes): `StaticAnalyzer.csproj`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `net10.0` connect `Community 0` to `Community 6`, `Community 9`, `Community 7`, `Community 11`?**
  _High betweenness centrality (0.042) - this node is a cross-community bridge._
- **Why does `Microsoft.NET.Sdk` connect `Community 0` to `Community 6`, `Community 9`, `Community 11`?**
  _High betweenness centrality (0.042) - this node is a cross-community bridge._
- **Why does `Microsoft.NET.Test.Sdk` connect `Community 0` to `Community 7`?**
  _High betweenness centrality (0.001) - this node is a cross-community bridge._
- **What connects `Configurator.csproj`, `@(KarpikModuleDependency->'%(ResolvedProjectPath)')`, `Karpik.Engine.Core.csproj` to the rest of the system?**
  _41 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.06540880503144654 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.05643564356435644 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.11363636363636363 - nodes in this community are weakly interconnected._