# Node Description Batch 8 of 11

Graphify is running in assistant/skill mode (no API key). You are the host
assistant (Claude Code / Codex / Gemini CLI). Read the prompt below and write
your JSON answer to the answer file.

## Prompt

You are documenting nodes in a knowledge graph.
For each entry below, write ONE concise factual plain-language sentence
describing what it is or does. Use only the provided context.
For a code symbol (kind=code-symbol — a function, class, or constant),
describe what the function/symbol does based on its name, source location
and neighbors — e.g. "Resolves the configured ontology profile from graphify.yaml.".
For an entity node (any other kind — e.g. a person, place, event, object),
describe what the entity is and its role, grounded in its type, its
relations (neighbors) and the provided citations/evidence — e.g.
"Lady Carfax, a wealthy heiress who disappears en route to Lausanne.".
Ground entity descriptions in the citations/evidence when present; do not
speculate beyond the context, so a node with no supporting context may be
left out of the reply.
LANGUAGE: each entry has a `lang=` marker giving the language of its source.
Write that entry's description in EXACTLY that language. Do not translate to
a single common language — match each node's source language individually.
No marketing language.
Respond ONLY with a JSON object mapping each node id (as a string) to its
one-sentence description — no prose, no markdown fences.

- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@e272a8d12db9b6b66da2fa536ca90bd03aba3cff": "e272a8d 3" | kind=Commit | source=git | neighbors=[98ffc66 3, codex/threaded-client-runtime, main, v-0.5, a8e92d2 4] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@f1ee038a973064b290b920ad35afa4402536d19a": "f1ee038 fix" | kind=Commit | source=git | neighbors=[bb4e5d4 2, codex/threaded-client-runtime, main, v-0.5, 2e6ff63 Fix scene] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@f62a2aa664a0d8a72f725589bd76fe1243dab8bc": "f62a2aa rename" | kind=Commit | source=git | neighbors=[30e1bb2 Fix stupid bug in Drawe :), codex/threaded-client-runtime, main, v-0.5, cf267c2 Add ArrayList] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@fbf130221c0ba5e18ed0fe183aeb5d3237677302": "fbf1302 more" | kind=Commit | source=git | neighbors=[d7d31b2 use codex, codex/threaded-client-runtime, main, v-0.5, 31dfc94 no code-review] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@ff8a3f86f53f6da84f79380d2be1f562ddc6ed10": "ff8a3f8 5" | kind=Commit | source=git | neighbors=[a8e92d2 4, codex/threaded-client-runtime, main, v-0.5, 92de2c7 6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@ff96c52611b8cd875d318a401e7eae049067c539": "ff96c52 Cleanup after ai" | kind=Commit | source=git | neighbors=[2256d14 a bit of ai analysis, v-0.5, c982726 Move launching runner into Karp…, Karpik.Engine.Core.csproj, Karpik.Engine.Core.Runner.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@ffb3d6657fe864cf98bb2a76030aa72c7aa5f32e": "ffb3d66 2" | kind=Commit | source=git | neighbors=[765359c 1, codex/threaded-client-runtime, main, v-0.5, 603320b a bit refactoring] | lang=en
- "loggermodule_loggermodule": "LoggerModule.csproj" | kind=code-symbol | source=Modules/Shared/LoggerModule/LoggerModule.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, 40f1a5f Merge pull request #3 from Karp…, net10.0, Microsoft.NET.Sdk] | lang=en
- "modding_lua_modding_lua": "Modding.Lua.csproj" | kind=code-symbol | source=Modules/Shared/Modding/Modding.Lua/Modding.Lua.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, net10.0, MoonSharp, Microsoft.NET.Sdk] | lang=en
- "network_client_core_network_client_core": "Network.Client.Core.csproj" | kind=code-symbol | source=Modules/Client/Network.Client/Network.Client.Core/Network.Client.Core.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, 40f1a5f Merge pull request #3 from Karp…, net10.0, Microsoft.NET.Sdk] | lang=en
- "network_codegen_network_codegen": "Network.Codegen.csproj" | kind=code-symbol | source=Network.Codegen/Network.Codegen/Network.Codegen.csproj:L1 | neighbors=[netstandard2.0, Microsoft.CodeAnalysis.Analyzers, Microsoft.CodeAnalysis.CSharp, Microsoft.CodeAnalysis.CSharp.Workspaces, Microsoft.NET.Sdk] | lang=en
- "network_server_litenetlib_network_server_litenetlib": "Network.Server.LiteNetLib.csproj" | kind=code-symbol | source=Modules/Server/Network.Server/Network.Server.LiteNetLib/Network.Server.LiteNetLib.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, 40f1a5f Merge pull request #3 from Karp…, net10.0, Microsoft.NET.Sdk] | lang=en
- "network_shared_core_network_shared_core": "Network.Shared.Core.csproj" | kind=code-symbol | source=Modules/Shared/Network.Shared/Network.Shared.Core/Network.Shared.Core.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, bb4e5d4 2, net10.0, Microsoft.NET.Sdk] | lang=en
- "network_shared_litenetlib_network_shared_litenetlib": "Network.Shared.LiteNetLib.csproj" | kind=code-symbol | source=Modules/Shared/Network.Shared.LiteNetLib/Network.Shared.LiteNetLib.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, net10.0, LiteNetLib, Microsoft.NET.Sdk] | lang=en
- "nuget_microsoft_codeanalysis_csharp": "Microsoft.CodeAnalysis.CSharp" | kind=code-symbol | source=Tools/StaticAnalyzer/StaticAnalyzer.csproj | neighbors=[Karpik.Engine.Core.Codegen.csproj, Network.Codegen.csproj, StatAndAbilities.Codegen.csproj, StaticAnalyzer.csproj, StaticAnalyzer.Tests.csproj] | lang=en
- "nuget_xunit_v3": "xunit.v3" | kind=code-symbol | source=templates/Karpik.Game/Tests/KarpikGame.Tests/KarpikGame.Tests.csproj | neighbors=[Karpik.Editor.Tests.csproj, Karpik.Engine.Sdk.IntegrationTests.cspr…, Karpik.Engine.Sdk.Tasks.Tests.csproj, Karpik.Launcher.Tests.csproj, KarpikGame.Tests.csproj] | lang=en
- "statandabilities_codegen_statandabilities_codegen": "StatAndAbilities.Codegen.csproj" | kind=code-symbol | source=StatAndAbilities.Codegen/StatAndAbilities.Codegen/StatAndAbilities.Codegen.csproj:L1 | neighbors=[netstandard2.0, Microsoft.CodeAnalysis.Analyzers, Microsoft.CodeAnalysis.CSharp, Microsoft.CodeAnalysis.CSharp.Workspaces, Microsoft.NET.Sdk] | lang=en
- "statandabilities_statandabilities": "StatAndAbilities.csproj" | kind=code-symbol | source=Modules/Shared/StatAndAbilities/StatAndAbilities.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, 40f1a5f Merge pull request #3 from Karp…, net10.0, Microsoft.NET.Sdk] | lang=en
- "tween_core_tween_core": "Tween.Core.csproj" | kind=code-symbol | source=Modules/Shared/Tween/Tween.Core/Tween.Core.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, 40f1a5f Merge pull request #3 from Karp…, net10.0, Microsoft.NET.Sdk] | lang=en
- "window_headless_window_headless": "Window.Headless.csproj" | kind=code-symbol | source=Modules/Client/Window/Window.Headless/Window.Headless.csproj:L1 | neighbors=[8b334ba Add headless graphics and nonbl…, d468ec2 Merge threaded client runtime, Input.Tests.csproj, net10.0, Microsoft.NET.Sdk] | lang=en
- "window_sdl2_window_sdl2": "Window.Sdl2.csproj" | kind=code-symbol | source=Modules/Client/Window/Window.Sdl2/Window.Sdl2.csproj:L1 | neighbors=[3207859 delete, 37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, net10.0, Microsoft.NET.Sdk] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@00be65c80ab9380242df7e06ff1bcd1f468b9b88": "00be65c fix: harden versioned SDK review findings" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 9330f26 docs: record final review remed…, ca0d1c3 fix: remove remaining game-root…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@00d8066e6684dcb1cfb325166a5422e081ec3e53": "00d8066 fix: harden editor runtime ownership" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 5d6a819 test: narrow editor symlink cap…, 5426dfe fix: enforce external editor ru…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@05c206fd551cf4cd582b93b9154de2cf44d9888f": "05c206f refactor: complete engine-only configurator migration" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, ca0d1c3 fix: remove remaining game-root…, 250c587 test: observe project switch be…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@127441d18d03fbcb62dad5562fd22f671888df66": "127441d fix: isolate worker lifecycle generations" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, df653d2 fix: harden worker generation o…, f201399 fix: serialize worker lifecycle…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@187cd8ba85937f8245b7d8038a6a0b385d882f3a": "187cd8b graphify" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, b68f055 docs: define external game SDK …, 6827c24 editor] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@19f8506af4ab686257f1dcf2ed1b20e754d0e4b8": "19f8506 fix: serialize lifecycle callback commits" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, d9ad608 test: await deferred worker dis…, df653d2 fix: harden worker generation o…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@250c587a8641c31144ffc3684aa9a688c6dbca45": "250c587 test: observe project switch before publication" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 05c206f refactor: complete engine-only …, 89680bb test: prove external editor pro…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@25ba8704dc9c663d65b9d21047d5afa4463811a9": "25ba870 Add IsEnabled check for ModContainer" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, v-0.5, 02775e2 Add more consistent behaviour f…, b676b3a Fix FirstPeer throws exception …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@27d6be7960251fefce4018be64ce64c7063c757b": "27d6be7 docs: record SDK migration milestone 1" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 7e175a2 feat: package Karpik custom MSB…, cdc613f fix: restrict project model par…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2e6efe380b5df0f5698792c58713cb4363a4489b": "2e6efe3 fix hot reload building" | kind=Commit | source=git | neighbors=[v-0.5, c6d7dfd Make server greate again (compi…, Karpik.Engine.Client.Publish.csproj, c982726 Move launching runner into Karp…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@31ffd9f3f10c573a8b1ffbb3cecbebd7b52d4e3a": "31ffd9f fix: isolate external SDK integration processes" | kind=Commit | source=git | neighbors=[20fbcfb feat: add external Karpik game …, codex/versioned-sdk-external-projects, v-0.6, 3578d3e feat: move runtime bundle owner…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@3369d88fd597c4136358d624bce9f0d5d5e46b03": "3369d88 test: update handoff fixture for module catalog" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 71cba74 docs: record remediation accept…, b54f376 chore: remove review workspace …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@4ba388669b34233c8ccb2a57df880ec366c81068": "4ba3886 docs: design editor console message copying" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, bd39266 feat: add transactional externa…, 5a924c1 chore: refresh Graphify cache] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@523ee037a1eb6be16b85eb76596d217fac5d06b6": "523ee03 Update Readme" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, v-0.5, 40f1a5f Merge pull request #3 from Karp…, 94d1cd8 Update Readme] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5281c500b51775289ae2d101c6405dbb3e54ae62": "5281c50 docs: plan external game SDK migration" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 9794654 chore: ignore local worktrees, b68f055 docs: define external game SDK …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5426dfe68eda11139e65dbb1a1d41a1f8c6850c8": "5426dfe fix: enforce external editor runtime ownership" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 00d8066 fix: harden editor runtime owne…, c6f98a6 docs: record R1 remediation com…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5a924c1701a4acda2b2615202c48924829777e6a": "5a924c1 chore: refresh Graphify cache" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 4ba3886 docs: design editor console mes…, af3f38d fix: retain timed out msbuild p…] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5d6a819c5d0a34cd658a5b075d5e0bffbc95b9f4": "5d6a819 test: narrow editor symlink capability skip" | kind=Commit | source=git | neighbors=[00d8066 fix: harden editor runtime owne…, codex/versioned-sdk-external-projects, v-0.6, 8d4ee33 fix: reject dangling runtime bu…] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5f82b0869c0ec240afcb3bb0dcacfa6e81695ea5": "5f82b08 docs: plan Milestone 8B-8D remediation" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 90ebc25 docs: number remediation tasks, a420207 refactor: migrate MyGame sample…] | lang=en

## Instructions

Write a single JSON object mapping each node id to a one-sentence description
to: C:\Users\artem\RiderProjects\KarpikEngine\.graphify\description-instructions\batch-007.json

Keep each description factual and concise (one sentence). No markdown, no prose
outside the JSON object. It is acceptable to omit a node if context is
insufficient — but include every node you can ground confidently.

Example answer format:
```json
{
  "node_id_1": "Resolves the configured ontology profile from graphify.yaml.",
  "node_id_2": "Colonel James Barclay, an antagonist in The Crooked Man."
}
```
