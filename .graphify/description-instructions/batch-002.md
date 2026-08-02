# Node Description Batch 3 of 11

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

- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@dd91c7fbef38778a25710063cc8f3c849ea2a466": "dd91c7f ide visablity bug" | kind=Commit | source=git | neighbors=[3b6d0c3 MODULE, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@ee73904904ed3ed44825d97be75172f113708d4e": "ee73904 IRenderPrepare" | kind=Commit | source=git | neighbors=[5b4e0a9 docs: record scheduler runtime …, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "configurator_tests_configurator_tests": "Configurator.Tests.csproj" | kind=code-symbol | source=Configurator.Tests/Configurator.Tests.csproj:L1 | neighbors=[37ecb6c Merge branch 'v-0.4', 3b6d0c3 MODULE, Configurator.csproj, net10.0, Microsoft.NET.Test.Sdk, xunit] | lang=en
- "karpik_editor_tests_karpik_editor_tests": "Karpik.Editor.Tests.csproj" | kind=code-symbol | source=Karpik.Editor.Tests/Karpik.Editor.Tests.csproj:L1 | neighbors=[3ee0b87 feat: complete Milestone 8C — r…, 6827c24 editor, net10.0, Karpik.Editor.csproj, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio] | lang=en
- "karpik_engine_sdk_karpik_engine_sdk": "Karpik.Engine.Sdk.csproj" | kind=code-symbol | source=Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj:L1 | neighbors=[7e175a2 feat: package Karpik custom MSB…, aeea6a2 feat: add transactional engine …, ca8830d Fix editor preview runtime and …, ccde2d7 Dragon third party, some sdk fi…, Karpik.Engine.Sdk.IntegrationTests.cspr…, net10.0] | lang=en
- "karpikgame_server_karpikgame_server": "KarpikGame.Server.csproj" | kind=code-symbol | source=templates/Karpik.Game/Source/KarpikGame.Server/KarpikGame.Server.csproj:L1 | neighbors=[20fbcfb feat: add external Karpik game …, 3578d3e feat: move runtime bundle owner…, a420207 refactor: migrate MyGame sample…, aeabe44 fix: restore portable runtime b…, net10.0, KarpikGame.Shared.csproj] | lang=en
- "karpikgame_tests_karpikgame_tests": "KarpikGame.Tests.csproj" | kind=code-symbol | source=templates/Karpik.Game/Tests/KarpikGame.Tests/KarpikGame.Tests.csproj:L1 | neighbors=[20fbcfb feat: add external Karpik game …, net10.0, KarpikGame.Client.csproj, KarpikGame.Shared.csproj, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@06c795c8f726c5bfeb4c8204162d212fde34b11a": "06c795c build update" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 2fabb7c World expansion] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@0e99edeb6e088014ec0a8c1b5e82d20ef0fd94b4": "0e99ede Implement to SpriteRenderer" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 54ca817 Analyzer] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@144827d627b498a6e786d4f082853080b12e95cf": "144827d yes" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, ee9d8a0 forgot] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@14b7d737d995c42550868f6c3ab7c5fa87ac1ea5": "14b7d73 plan" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 92551fc ui?] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@14c4ad728cc114924f0deffef4faebf4c190dc73": "14c4ad7 Update Readme" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 750da0b changelog skill] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@186d6fc8a27c7a9291e26b3cb3776c1725ef5a19": "186d6fc 2" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 37ecb6c Merge branch 'v-0.4'] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1b730d443be4bfb7062fa6a712d653ebb305f175": "1b730d4 ValueJobHandle" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 5c1e7fd dependencies] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1ba4823eeeaf33b5a98f1463a210fcceaa767208": "1ba4823 Test? 3" | kind=Commit | source=git | neighbors=[173769b Test? 2, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1ddaca8c7d755b56dba657537849a0abf6c9bf2e": "1ddaca8 2" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, f8402c6 omg greate font draw] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1e3efd4bd1358f2acd2ef7ae7b6f608de90846d7": "1e3efd4 plan done!" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, ef92076 imgui plan] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1e567c15f39176eb6ff5c94547ffff0017f21547": "1e567c1 1" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, c43fa3d 1] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1e925aeb6c5785b16422a677f32b49c244f253dd": "1e925ae Benchmarks" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, a3fbd56 IJob] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@1ec4d087c788ffec67dabc93d33f883866bac63b": "1ec4d08 cleanup" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, b52a273 fix incorrect subscribtion] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@25181f4a078f8821ed7ab8a43bdb492b03df2fd2": "25181f4 graphify2" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 2201c51 0.5] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@260eafa3838582fcf9e70a2c4b335f5837a96f57": "260eafa Camera component" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, main, v-0.5, 37693d6 update todo, Graphics.Core.csproj, Input.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@26bf94eb3fcc76f0dc7e85ec30c6c82f1496a038": "26bf94e Add no aspect check" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, c53c4bc fix destroy] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@293aa747cd939b0938fb12aada6c798356c7b685": "293aa74 controller beta 5" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, d35f696 Rename] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2aa24a08c19d6447dbfee02b3c924797143a8c2d": "2aa24a0 controller beta" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, ea5c75d controller beta 2] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2bdfa0b60f8e0c869beb40a81e5a63f5280b9d1e": "2bdfa0b 1" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, a95a778 plan] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2fabb7cd9264725410b6b5cad2b1f1c01266b679": "2fabb7c World expansion" | kind=Commit | source=git | neighbors=[06c795c build update, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@3447c655d2bd95cf222ba3c99f834bfcbcf5a3a1": "3447c65 2" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, fe3b056 ui 2] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@3540abe9ac3c14251567994feba2bf918fb4ff67": "3540abe plan" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, cb6628b wrappers p1] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@3c1cd38d4c237035778f6d1f4484e046c5971dd5": "3c1cd38 feat: compose installed engine modules with game bundles" | kind=Commit | source=git | neighbors=[codex/versioned-sdk-external-projects, v-0.6, 75a747c feat: Milestone 8B - external E…, Directory.Build.targets, Karpik.Engine.Core.Runner.csproj, Karpik.Engine.Core.Runner.Tests.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@3d79774098a7ef5f4b0638311d60b1ab772a6ed0": "3d79774 Make demomodule great again" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 50ec300 1] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@3e759ef90dfe6bc3d49b511f522842f69d0e422f": "3e759ef debug" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, caab0dd Work stealing deque] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@469aea9382f33d6c7ef9091e5fa5dc5f8bfff6be": "469aea9 camera, demo scene, disablable super hot reload" | kind=Commit | source=git | neighbors=[2cba8e9 Physics scene, codex/threaded-client-runtime, main, v-0.5, 30e1bb2 Fix stupid bug in Drawe :), Karpik.Engine.Core.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@4e7805fefbf784ee6d74baa888ad7a09eb0988d8": "4e7805f 2" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 3540abe plan] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@50ec3001f48ac400b656e6cee5678fc3838440ed": "50ec300 1" | kind=Commit | source=git | neighbors=[3d79774 Make demomodule great again, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@59e8dcf11bf59212dba213480b8d72cac82aacfb": "59e8dcf Replace v0.3 release notes with v0.4" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, b7fa5bc superpowers] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5a43353349bd27c1ca9a4d64cb22785f553bb4ff": "5a43353 fix sprites" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 95a6ceb y flip] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5ad4bd2ddb3040d8c9eeacafdb93317546840b63": "5ad4bd2 1" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 3447c65 2] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5b4e0a9be40836b017b02aa91c972fb79e80ed2c": "5b4e0a9 docs: record scheduler runtime fixes" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, ee73904 IRenderPrepare] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5c1e7fde34f17e82c1d1520909624e58d6e74c90": "5c1e7fd dependencies" | kind=Commit | source=git | neighbors=[1b730d4 ValueJobHandle, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en

## Instructions

Write a single JSON object mapping each node id to a one-sentence description
to: C:\Users\artem\RiderProjects\KarpikEngine\.graphify\description-instructions\batch-002.json

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
