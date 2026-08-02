# Node Description Batch 4 of 11

Graphify is running in assistant/skill mode (no API key). You are the host
assistant (Claude Code / Codex / Gemini CLI). Read the prompt below and write
your JSON answer to the answer file.

## Prompt

You are documenting nodes in a knowledge graph.
For each entry below, write ONE concise factual plain-language sentence
describing what it is or does. Use only the provided context.
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

- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5cd417401e02d7fe37079bc7fc6a6bb0681b547b": "5cd4174 fix read->write confilct" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 26bf94e Add no aspect check] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5e39f0bc80f3c9a5c39a9bc2f71b2e9d5e57c151": "5e39f0b warm up" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 6a6e989 7] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5f0a59ca84f5aa73c38effb667060456e76a3f4e": "5f0a59c server hot reload" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 2aa24a0 controller beta] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@5f85b08112cbd3adbdc5ed2d8d3a6f24b57c2820": "5f85b08 roadmap" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 3d79774 Make demomodule great again] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@60b6decdd2edc5836b72256bb4fe4cd36e45088d": "60b6dec imgui done" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 5f85b08 roadmap] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@64f3e5014b6281930ba856ec0c23b34aa224dd54": "64f3e50 12" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 1e3efd4 plan done!] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@64fa65ed5c381bf873b48ee2841f8fc3e5ac5450": "64fa65e lifecycle tests" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, b90bf3f tests] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@67a9ccf96f1139587ed01e97163a1948b8a098c2": "67a9ccf log" | kind=Commit | source=git | neighbors=[2fabb7c World expansion, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@6c3d1c573ce0d7041c95086a268d7755d674b336": "6c3d1c5 graphify" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 25181f4 graphify2] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@6e9d00e10ffcc86ebf10ff01732fd4905f848ab3": "6e9d00e controller beta 3" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 9d57ed8 controller beta 4] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@70c93ed3f8d213dea9cca24d9a279ebb4badacf0": "70c93ed backlog" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, ba0f67b analyzer] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@73043e459541fc6a12f6520a0aba940ddf5bcf42": "73043e4 update plan" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, f7eac8f update docs] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@750da0b7286e5f0e339817616b02499ed806e18c": "750da0b changelog skill" | kind=Commit | source=git | neighbors=[14c4ad7 Update Readme, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@79b4a51aee437245097f5b4569901280e8247ffc": "79b4a51 Stage hot reload modules as complete versions" | kind=Commit | source=git | neighbors=[4421b04 Run hot reload worker simulatio…, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.6, 55cd61a Clean up stale hot reload modul…] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@7c013a904cea564ae9d77b7ad21da4e3b8adde0c": "7c013a9 1" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 186d6fc 2] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@7e3ab349ee0d45b86f0017ccf00a6bd388650807": "7e3ab34 hot reload reconnection" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 5f0a59c server hot reload] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@7eff6aef752e6ba8638617afc91d8b4aa2fa9dc7": "7eff6ae clean world" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 91c7738 clean 2] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@80542715ca4f7bcb893f75f3937f51c966ba4d31": "8054271 update plan" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, d8fe6ac modules plan] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@91c773828c5974bda86e4a8de678ba92e1f975e4": "91c7738 clean 2" | kind=Commit | source=git | neighbors=[7eff6ae clean world, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@92551fcd8cc81321d40954d01630b62f21279753": "92551fc ui?" | kind=Commit | source=git | neighbors=[14b7d73 plan, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@95a6ceb80c7d59361ba86316231da6702d1f183f": "95a6ceb y flip" | kind=Commit | source=git | neighbors=[5a43353 fix sprites, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@971d1d0596ee00773d3763e342db5be77ced776e": "971d1d0 step 1" | kind=Commit | source=git | neighbors=[92551fc ui?, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@9745f3f87ef127b9025bd4a901cf3b20d167d7b3": "9745f3f Add controllable graphics load probe" | kind=Commit | source=git | neighbors=[47ffb3d Add threaded client frame metri…, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.6, ba90c6a Aggregate threaded client frame…] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@9801c0ed6438b4d9afd63b19ab3a319c7d3b84c9": "9801c0e Test?" | kind=Commit | source=git | neighbors=[2260a53 atlas support, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, 173769b Test? 2] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@9d57ed8070052fcd8bd16b944df113341f120571": "9d57ed8 controller beta 4" | kind=Commit | source=git | neighbors=[6e9d00e controller beta 3, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@a3a8bacc2409328fde1ad829476e0fe0175c4ed5": "a3a8bac Copy frame timing summary to clipboard" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.6, 82e244a Fix frame timing aggregate stat…, Graphics.Core.Tests.csproj] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@a3fbd56bd131ca45ec729efc08154cc0107859db": "a3fbd56 IJob" | kind=Commit | source=git | neighbors=[1e925ae Benchmarks, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@a6c95a5b8ce03ee3641e37ec9e75183f55d5033f": "a6c95a5 ai restrictions" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, f9bbda2 plan update] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@a95a778fc678ddfa257791b52ab81222b49d427f": "a95a778 plan" | kind=Commit | source=git | neighbors=[2bdfa0b 1, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@ae72c6c92c86fe0aa0ca86087ed204943b0bbad7": "ae72c6c fix" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, dd41ecb 1] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@b52a273d7c9e6e225ee0285721c8f1d2bf48f2b7": "b52a273 fix incorrect subscribtion" | kind=Commit | source=git | neighbors=[1ec4d08 cleanup, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@b7fa5bc350f5c89015956cdfe135c1e58e711206": "b7fa5bc superpowers" | kind=Commit | source=git | neighbors=[59e8dcf Replace v0.3 release notes with…, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@bee2a8eb89201c81097b1151713cab31af557ff5": "bee2a8e tests" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 1e925ae Benchmarks] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c393a4a964cb095cc535fb9a476558eb95065898": "c393a4a 6" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 941313f simplify] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c3a241955f47e61e200a70b2f3ac89afa1ba83da": "c3a2419 босс, я устал" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6, 144827d yes] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c43fa3db7d95e5f5451cf9a36d87280b360af4ca": "c43fa3d 1" | kind=Commit | source=git | neighbors=[1e567c1 1, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c53c4bce32f1ef0a90ea40b8f9052993027b2c71": "c53c4bc fix destroy" | kind=Commit | source=git | neighbors=[26bf94e Add no aspect check, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c55870e06aab4a88d2fcca8bdbec6a0bdabca78d": "c55870e Промежуточно" | kind=Commit | source=git | neighbors=[codex/threaded-client-runtime, main, v-0.5, 765359c 1, Graphics.Core.csproj, Graphics.OpenGL.csproj] | lang=en
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c8a85c9b22dc4369ced334b8398a0a0401ee3b20": "c8a85c9 skill" | kind=Commit | source=git | neighbors=[971d1d0 step 1, codex/threaded-client-runtime, codex/versioned-sdk-external-projects, main, v-0.5, v-0.6] | lang=pt
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@ca8830dc7bf3ef368e011c645630b1220b145a62": "ca8830d Fix editor preview runtime and ECS SDK packaging" | kind=Commit | source=git | neighbors=[01729bf Add sdk local publish script, AssetManagement.Core.csproj, v-0.6, 4158570 graphify, Karpik.Engine.Core.Runner.Tests.csproj, Karpik.Engine.Sdk.csproj] | lang=en

## Instructions

Write a single JSON object mapping each node id to a one-sentence description
to: C:\Users\artem\RiderProjects\KarpikEngine\.graphify\description-instructions\batch-003.json

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
