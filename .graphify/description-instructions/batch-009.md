# Node Description Batch 10 of 11

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
Write every description in Portuguese (pt). Do not switch languages.
No marketing language.
Respond ONLY with a JSON object mapping each node id (as a string) to its
one-sentence description — no prose, no markdown fences.

- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@201b73ebc5894679b32ef9520a1e5f59078123ee": "201b73e mb fix crash on hot reload sometimes" | kind=Commit | source=git | neighbors=[v-0.5, 6b96307 Debugger attachment, 2a2fa87 omg]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2256d1458ce97e6b73beee95e8b31f5c273353bc": "2256d14 a bit of ai analysis" | kind=Commit | source=git | neighbors=[10f4448 update, v-0.5, ff96c52 Cleanup after ai]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2a2fa87f9eecad3492151902ba3dc7809a6ca04d": "2a2fa87 omg" | kind=Commit | source=git | neighbors=[v-0.5, 201b73e mb fix crash on hot reload some…, 8b8acbf test]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@2f65ab992b89908fa944fa6b7018b1f5ec397325": "2f65ab9 Copy status text" | kind=Commit | source=git | neighbors=[v-0.6, 700ace9 fix, 71cba74 docs: record remediation accept…]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@41585708325f4cbff500969657548a9b9d060281": "4158570 graphify" | kind=Commit | source=git | neighbors=[v-0.6, 98a7103 Localization, ca8830d Fix editor preview runtime and …]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@6b9630751666f3ff9675b7c1b5d881415dd622e5": "6b96307 Debugger attachment" | kind=Commit | source=git | neighbors=[201b73e mb fix crash on hot reload some…, v-0.5, 10f4448 update]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@c982726fe2d983bdbc8a1f4081895880a3907113": "c982726 Move launching runner into Karpik.Engine.Core" | kind=Commit | source=git | neighbors=[v-0.5, 2e6efe3 fix hot reload building, ff96c52 Cleanup after ai]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@d1b97d0e5e0d7987c1395a59cecc00f4ccaa801a": "d1b97d0 commit" | kind=Commit | source=git | neighbors=[6c54941 1, v-0.5, 8b8acbf test]
- "commit:repo:github.com/Karpiklalloly/Karpik.Engine@d473ed17c5a2643e541642b201c0ddb78054a06a": "d473ed1 Update Tests" | kind=Commit | source=git | neighbors=[98a7103 Localization, v-0.6, ccde2d7 Dragon third party, some sdk fi…]
- "configurator_configurator": "Configurator.csproj" | kind=code-symbol | source=Configurator/Configurator.csproj:L1 | neighbors=[net10.0, Microsoft.NET.Sdk, Configurator.Tests.csproj]
- "unsafeutilities_unsafeutilities": "UnsafeUtilities.csproj" | kind=code-symbol | source=Modules/Shared/UnsafeUtilities/UnsafeUtilities.csproj:L1 | neighbors=[cf267c2 Add ArrayList, net10.0, Microsoft.NET.Sdk]
- "c_users_artem_riderprojects_karpikengine_first_parties_karpik_jobs_karpik_jobs_karpik_jobs_csproj": "Karpik.Jobs.csproj" | kind=code-symbol | source=Karpik.Engine.Core/Karpik.Engine.Core.csproj | neighbors=[DragonExtensions.csproj, Karpik.Engine.Core.csproj]
- "modding_testmodule_modding_testmodule": "Modding.TestModule.csproj" | kind=code-symbol | source=Modules/Shared/Modding/Modding.TestModule/Modding.TestModule.csproj:L1 | neighbors=[net10.0, Microsoft.NET.Sdk]
- "nuget_avalonia": "Avalonia" | kind=code-symbol | source=Karpik.Launcher/Karpik.Launcher.csproj | neighbors=[Karpik.Editor.csproj, Karpik.Launcher.csproj]
- "nuget_avalonia_desktop": "Avalonia.Desktop" | kind=code-symbol | source=Karpik.Launcher/Karpik.Launcher.csproj | neighbors=[Karpik.Editor.csproj, Karpik.Launcher.csproj]
- "nuget_avalonia_fonts_inter": "Avalonia.Fonts.Inter" | kind=code-symbol | source=Karpik.Launcher/Karpik.Launcher.csproj | neighbors=[Karpik.Editor.csproj, Karpik.Launcher.csproj]
- "nuget_avalonia_themes_fluent": "Avalonia.Themes.Fluent" | kind=code-symbol | source=Karpik.Launcher/Karpik.Launcher.csproj | neighbors=[Karpik.Editor.csproj, Karpik.Launcher.csproj]
- "nuget_litenetlib": "LiteNetLib" | kind=code-symbol | source=Modules/Shared/Network.Shared.LiteNetLib/Network.Shared.LiteNetLib.csproj | neighbors=[Network.Client.LiteNetLib.csproj, Network.Shared.LiteNetLib.csproj]
- "nuget_microsoft_build_framework": "Microsoft.Build.Framework" | kind=code-symbol | source=Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj | neighbors=[Karpik.Engine.Sdk.Tasks.csproj, Karpik.Engine.Sdk.Tasks.Tests.csproj]
- "nuget_microsoft_build_utilities_core": "Microsoft.Build.Utilities.Core" | kind=code-symbol | source=Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj | neighbors=[Karpik.Engine.Sdk.Tasks.csproj, Karpik.Engine.Sdk.Tasks.Tests.csproj]
- "nuget_reactiveui_avalonia": "ReactiveUI.Avalonia" | kind=code-symbol | source=Karpik.Launcher/Karpik.Launcher.csproj | neighbors=[Karpik.Editor.csproj, Karpik.Launcher.csproj]
- "c_users_artem_riderprojects_karpikengine_dragon_dragon_csproj": "Dragon.csproj" | kind=code-symbol | source=Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj | neighbors=[StaticAnalyzer.Tests.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_spirv_src_veldrid_spirv_veldrid_spirv_csproj": "Veldrid.SPIRV.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_src_veldrid_imgui_veldrid_imgui_csproj": "Veldrid.ImGui.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_src_veldrid_openglbindings_veldrid_openglbindings_csproj": "Veldrid.OpenGLBindings.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_src_veldrid_sdl2_veldrid_sdl2_csproj": "Veldrid.SDL2.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_src_veldrid_startuputilities_veldrid_startuputilities_csproj": "Veldrid.StartupUtilities.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_src_veldrid_utilities_veldrid_utilities_csproj": "Veldrid.Utilities.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_first_parties_kveldrid_src_veldrid_veldrid_csproj": "Veldrid.csproj" | kind=code-symbol | source=TestVeldrid/TestVeldrid.csproj | neighbors=[TestVeldrid.csproj]
- "c_users_artem_riderprojects_karpikengine_karpikmoduledependency_resolvedprojectpath": "@(KarpikModuleDependency->'%(ResolvedProjectPath)')" | kind=code-symbol | source=Directory.Build.targets | neighbors=[Directory.Build.targets]
- "c_users_artem_riderprojects_karpikengine_third_parties_dragonecs_dragonecs_csproj": "DragonECS.csproj" | kind=code-symbol | source=DragonExtensions/DragonExtensions.csproj | neighbors=[DragonExtensions.csproj]
- "karpik_game_directory_solution": "Directory.Solution.targets" | kind=code-symbol | source=templates/Karpik.Game/Directory.Solution.targets:L1 | neighbors=[20fbcfb feat: add external Karpik game …]
- "nuget_aether_physics2d": "Aether.Physics2D" | kind=code-symbol | source=Modules/Shared/Physics/Physics2D.Aether2D/Physics2D.Aether2D.csproj | neighbors=[Physics2D.Aether2D.csproj]
- "nuget_dock_avalonia": "Dock.Avalonia" | kind=code-symbol | source=Karpik.Editor/Karpik.Editor.csproj | neighbors=[Karpik.Editor.csproj]
- "nuget_dock_avalonia_themes_fluent": "Dock.Avalonia.Themes.Fluent" | kind=code-symbol | source=Karpik.Editor/Karpik.Editor.csproj | neighbors=[Karpik.Editor.csproj]
- "nuget_dock_model_reactiveui": "Dock.Model.ReactiveUI" | kind=code-symbol | source=Karpik.Editor/Karpik.Editor.csproj | neighbors=[Karpik.Editor.csproj]
- "nuget_dock_serializer_systemtextjson": "Dock.Serializer.SystemTextJson" | kind=code-symbol | source=Karpik.Editor/Karpik.Editor.csproj | neighbors=[Karpik.Editor.csproj]
- "nuget_microsoft_extensions_dependencyinjection": "Microsoft.Extensions.DependencyInjection" | kind=code-symbol | source=Karpik.Engine.Core/Karpik.Engine.Core.csproj | neighbors=[Karpik.Engine.Core.csproj]
- "nuget_moonsharp": "MoonSharp" | kind=code-symbol | source=Modules/Shared/Modding/Modding.Lua/Modding.Lua.csproj | neighbors=[Modding.Lua.csproj]
- "nuget_stbimagesharp": "StbImageSharp" | kind=code-symbol | source=Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj | neighbors=[Graphics.Core.csproj]

## Instructions

Write a single JSON object mapping each node id to a one-sentence description
to: C:\Users\artem\RiderProjects\KarpikEngine\.graphify\description-instructions\batch-009.json

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
