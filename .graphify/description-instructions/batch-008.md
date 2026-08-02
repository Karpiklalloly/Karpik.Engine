# Node Description Batch 9 of 11

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

- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@604d8b5258de8646246b4d6fb410b98eff784b1c": "604d8b5 docs: record R2 remediation completion" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 89680bb test: prove external editor pro…, 8d4ee33 fix: reject dangling runtime bu…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@700ace915a5ccad57a0f53396764c1318d4bb5d6": "700ace9 fix" | kind=Commit | source=git | neighbors=[2f65ab9 Copy status text, v-0.6, 01729bf Add sdk local publish script, Karpik.Launcher.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@71cba74efffe221f3b174a274a4b51040e4f7a48": "71cba74 docs: record remediation acceptance" | kind=Commit | source=git | neighbors=[3369d88 test: update handoff fixture fo…, codex/versioned-sdk-external-projects, v-0.6, 2f65ab9 Copy status text] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@86abdce12600fb3b669cd747604dfb9093e6d2ba": "86abdce fix: enforce isolated module payload contract" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 20fbcfb feat: add external Karpik game …, aeea6a2 feat: add transactional engine …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@896782e00a07ae76a9ce8b685eee4838163c3138": "896782e fix: retain project inputs through evaluation" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, af3f38d fix: retain timed out msbuild p…, fc825e2 fix: harden editor project cont…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@89680bbad4ffccb4dfac9c23eaa17403a094a220": "89680bb test: prove external editor project switch cleanup" | kind=Commit | source=git | neighbors=[604d8b5 docs: record R2 remediation com…, codex/versioned-sdk-external-projects, v-0.6, 250c587 test: observe project switch be…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@8b573e2066243a4c458c140d0b267332460b244d": "8b573e2 docs: split milestone 8 into staged delivery" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 3c1cd38 feat: compose installed engine …, b923a21 feat: add version-aware Karpik …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@8b8acbf3088ffe22dabbbd8eec428bafb6c374ce": "8b8acbf test" | kind=Commit | source=git | neighbors=[v-0.5, 2a2fa87 omg, Karpik.Engine.Core.Runner.csproj, d1b97d0 commit] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@8d4ee335f9f88ea82c507075d798d33315c91a93": "8d4ee33 fix: reject dangling runtime bundle links" | kind=Commit | source=git | neighbors=[5d6a819 test: narrow editor symlink cap…, codex/versioned-sdk-external-projects, v-0.6, 604d8b5 docs: record R2 remediation com…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@90ebc25623c6288654350d1823641375ddebb180": "90ebc25 docs: number remediation tasks" | kind=Commit | source=git | neighbors=[5f82b08 docs: plan Milestone 8B-8D reme…, codex/versioned-sdk-external-projects, v-0.6, aeabe44 fix: restore portable runtime b…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@9330f26d29c141eaa80336fc7c0163e45f05440a": "9330f26 docs: record final review remediation" | kind=Commit | source=git | neighbors=[00be65c fix: harden versioned SDK revie…, codex/versioned-sdk-external-projects, v-0.6, b54f376 chore: remove review workspace …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@94d1cd8540ea073bd06fe82098b3e3dde4a5a320": "94d1cd8 Update Readme" | kind=Commit | source=git | neighbors=[1342a16 A bit of physics plans, codex/threaded-client-runtime, v-0.5, 523ee03 Update Readme] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@97946549fd54bd82e07144e39113bbf3a8296dd0": "9794654 chore: ignore local worktrees" | kind=Commit | source=git | neighbors=[5281c50 docs: plan external game SDK mi…, codex/versioned-sdk-external-projects, v-0.6, c1de8f3 chore: ignore subagent developm…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@98a710372181ffceb040824ebc5a282eb1b6fbfe": "98a7103 Localization" | kind=Commit | source=git | neighbors=[4158570 graphify, v-0.6, d473ed1 Update Tests, Karpik.Launcher.csproj] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@af3f38d317215dc9c684a4b91a9365b87cf72efb": "af3f38d fix: retain timed out msbuild process ownership" | kind=Commit | source=git | neighbors=[896782e fix: retain project inputs thro…, codex/versioned-sdk-external-projects, v-0.6, 5a924c1 chore: refresh Graphify cache] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@b54f3760b69887111f0f0134924d867e6bdf0a94": "b54f376 chore: remove review workspace artifact" | kind=Commit | source=git | neighbors=[9330f26 docs: record final review remed…, codex/versioned-sdk-external-projects, v-0.6, 3369d88 test: update handoff fixture fo…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@b676b3a626f2506b7ed2f695a8d2b118b1e7ac0e": "b676b3a Fix FirstPeer throws exception if there is no server connection" | kind=Commit | source=git | neighbors=[40f1a5f Merge pull request #3 from Karp…, codex/threaded-client-runtime, v-0.5, 25ba870 Add IsEnabled check for ModCont…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@b68f055d63a3424835d40bedfde2367d19e25faf": "b68f055 docs: define external game SDK architecture" | kind=Commit | source=git | neighbors=[187cd8b graphify, codex/versioned-sdk-external-projects, v-0.6, 5281c50 docs: plan external game SDK mi…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@b87ce373c668b7c8bcba1a3a7d3b23540fa6dbb2": "b87ce37 Update configurator" | kind=Commit | source=git | neighbors=[AutoGenerated.targets, v-0.5, 59e869e fix, c6d7dfd Make server greate again (compi…] | lang=fr
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@bc5c33b506290e87fa2a5c42ab158992399a8716": "bc5c33b feat: complete Milestone 8B — ECS state preservation, project switch, c…" | kind=Commit | source=git | neighbors=[75a747c feat: Milestone 8B - external E…, codex/versioned-sdk-external-projects, v-0.6, 3ee0b87 feat: complete Milestone 8C — r…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c1de8f346754641f48877b789b5c1a8bd7caca53": "c1de8f3 chore: ignore subagent development ledger" | kind=Commit | source=git | neighbors=[9794654 chore: ignore local worktrees, codex/versioned-sdk-external-projects, v-0.6, 5765d51 feat: add reusable Karpik game …] | lang=nl
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c6d7dfd40cd246d18193eceb27d01e7bc5fe6c78": "c6d7dfd Make server greate again (compilatable)" | kind=Commit | source=git | neighbors=[2e6efe3 fix hot reload building, v-0.5, b87ce37 Update configurator, Karpik.Engine.Server.Publish.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c6f98a64b7a6737a11eb0b37fb81344f48249129": "c6f98a6 docs: record R1 remediation completion" | kind=Commit | source=git | neighbors=[561f17e fix: make linked-mod capability…, codex/versioned-sdk-external-projects, v-0.6, 5426dfe fix: enforce external editor ru…] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@cdc613ffa25e5d438399436f695f5ccee6f4794b": "cdc613f fix: restrict project model parsing and cycle diagnostics" | kind=Commit | source=git | neighbors=[5765d51 feat: add reusable Karpik game …, codex/versioned-sdk-external-projects, v-0.6, 27d6be7 docs: record SDK migration mile…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@d9ad60844a84662d577babeeb054c010ded3f072": "d9ad608 test: await deferred worker disposal" | kind=Commit | source=git | neighbors=[19f8506 fix: serialize lifecycle callba…, codex/versioned-sdk-external-projects, v-0.6, 3fadd98 feat: add transactional editor …] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@df653d23c48ba70de2d30a80f56be2eb179f3e81": "df653d2 fix: harden worker generation ownership" | kind=Commit | source=git | neighbors=[127441d fix: isolate worker lifecycle g…, codex/versioned-sdk-external-projects, v-0.6, 19f8506 fix: serialize lifecycle callba…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@fc825e2f3f1f2522da95cb100595ad5b86f7bed8": "fc825e2 fix: harden editor project context boundaries" | kind=Commit | source=git | neighbors=[3fadd98 feat: add transactional editor …, codex/versioned-sdk-external-projects, v-0.6, 896782e fix: retain project inputs thro…] | lang=en
- "debugmodule_debugmodule": "DebugModule.csproj" | kind=code-symbol | source=Modules/Shared/DebugModule/DebugModule.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, net10.0, Microsoft.NET.Sdk] | lang=en
- "framework_netstandard2_0": "netstandard2.0" | kind=entity | source=Tools/StaticAnalyzer/StaticAnalyzer.csproj | neighbors=[Karpik.Engine.Core.Codegen.csproj, Network.Codegen.csproj, StatAndAbilities.Codegen.csproj, StaticAnalyzer.csproj] | lang=en
- "graphics_headless_graphics_headless": "Graphics.Headless.csproj" | kind=code-symbol | source=Modules/Client/Graphics/Graphics.Headless/Graphics.Headless.csproj:L1 | neighbors=[8b334ba Add headless graphics and nonbl…, d468ec2 Merge threaded client runtime, net10.0, Microsoft.NET.Sdk] | lang=en
- "karpik_engine_core_runner_testworker_karpik_engine_core_runner_testworker": "Karpik.Engine.Core.Runner.TestWorker.csproj" | kind=code-symbol | source=Karpik.Engine.Core.Runner.TestWorker/Karpik.Engine.Core.Runner.TestWorker.csproj:L1 | neighbors=[f201399 fix: serialize worker lifecycle…, Karpik.Engine.Core.Runner.Tests.csproj, net10.0, Microsoft.NET.Sdk] | lang=en
- "karpikgame_client_launcher_karpikgame_client_launcher": "KarpikGame.Client.Launcher.csproj" | kind=code-symbol | source=templates/Karpik.Game/Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj:L1 | neighbors=[ccde2d7 Dragon third party, some sdk fi…, net10.0, KarpikGame.Client.csproj, Karpik.Engine.Sdk] | lang=en
- "karpikgame_server_launcher_karpikgame_server_launcher": "KarpikGame.Server.Launcher.csproj" | kind=code-symbol | source=templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj:L1 | neighbors=[ccde2d7 Dragon third party, some sdk fi…, net10.0, KarpikGame.Server.csproj, Karpik.Engine.Sdk] | lang=en
- "network_server_core_network_server_core": "Network.Server.Core.csproj" | kind=code-symbol | source=Modules/Server/Network.Server/Network.Server.Core/Network.Server.Core.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, net10.0, Microsoft.NET.Sdk] | lang=en
- "nuget_microsoft_codeanalysis_analyzers": "Microsoft.CodeAnalysis.Analyzers" | kind=code-symbol | source=Tools/StaticAnalyzer/StaticAnalyzer.csproj | neighbors=[Karpik.Engine.Core.Codegen.csproj, Network.Codegen.csproj, StatAndAbilities.Codegen.csproj, StaticAnalyzer.csproj] | lang=en
- "nuget_microsoft_codeanalysis_csharp_workspaces": "Microsoft.CodeAnalysis.CSharp.Workspaces" | kind=code-symbol | source=Tools/StaticAnalyzer/StaticAnalyzer.csproj | neighbors=[Karpik.Engine.Core.Codegen.csproj, Network.Codegen.csproj, StatAndAbilities.Codegen.csproj, StaticAnalyzer.csproj] | lang=en
- "nuget_newtonsoft_json": "Newtonsoft.Json" | kind=code-symbol | source=Modules/Shared/Modding/Modding.Core/Modding.Core.csproj | neighbors=[AssetManagement.Core.csproj, Directory.Build.targets, ECS.Core.csproj, Modding.Core.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@01729bf06ca8dbfc167c2096a258ac2bc49d166d": "01729bf Add sdk local publish script" | kind=Commit | source=git | neighbors=[v-0.6, ca8830d Fix editor preview runtime and …, 700ace9 fix] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@10f4448c57cce3bd0dc1f632ca7f7cb27b831ab0": "10f4448 update" | kind=Commit | source=git | neighbors=[v-0.5, 2256d14 a bit of ai analysis, 6b96307 Debugger attachment] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1342a163c947483a0e9337b6bb56789f6f8c5b94": "1342a16 A bit of physics plans" | kind=Commit | source=git | neighbors=[v-0.5, 94d1cd8 Update Readme, 59e869e fix] | lang=pt

## Instructions

Write a single JSON object mapping each node id to a one-sentence description
to: C:\Users\artem\RiderProjects\KarpikEngine\.graphify\description-instructions\batch-008.json

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
