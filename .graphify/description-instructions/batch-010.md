# Node Description Batch 11 of 11

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
Write every description in English (en). Do not switch languages.
No marketing language.
Respond ONLY with a JSON object mapping each node id (as a string) to its
one-sentence description — no prose, no markdown fences.

- "sdk_solution": "Solution.targets" | kind=code-symbol | source=Karpik.Engine.Sdk/Sdk/Solution.targets:L1 | neighbors=[7e175a2 feat: package Karpik custom MSB…]
- "templates_directory_solution": "Directory.Solution.targets" | kind=code-symbol | source=Karpik.Engine.Sdk/Templates/Directory.Solution.targets:L1 | neighbors=[7e175a2 feat: package Karpik custom MSB…]
- "c_users_artem_riderprojects_karpikengine_configurator_configurator_csproj": "Configurator.csproj" | kind=code-symbol | source=Configurator.Tests/Configurator.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_dragonextensions_dragonextensions_csproj": "DragonExtensions.csproj" | kind=code-symbol | source=Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_editor_karpik_editor_csproj": "Karpik.Editor.csproj" | kind=code-symbol | source=Karpik.Editor.Tests/Karpik.Editor.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_core_generator_karpik_engine_core_codegen_karpik_engine_core_codegen_csproj": "Karpik.Engine.Core.Codegen.csproj" | kind=code-symbol | source=Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_core_karpik_engine_core_csproj": "Karpik.Engine.Core.csproj" | kind=code-symbol | source=Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_core_runner_karpik_engine_core_runner_csproj": "Karpik.Engine.Core.Runner.csproj" | kind=code-symbol | source=Modules/Client/Input.Tests/Input.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_core_runner_testworker_karpik_engine_core_runner_testworker_csproj": "Karpik.Engine.Core.Runner.TestWorker.csproj" | kind=code-symbol | source=Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_packager_karpik_engine_packager_csproj": "Karpik.Engine.Packager.csproj" | kind=code-symbol | source=Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_projectmodel_karpik_engine_projectmodel_csproj": "Karpik.Engine.ProjectModel.csproj" | kind=code-symbol | source=Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_sdk_karpik_engine_sdk_csproj": "Karpik.Engine.Sdk.csproj" | kind=code-symbol | source=Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_sdk_tasks_karpik_engine_sdk_tasks_csproj": "Karpik.Engine.Sdk.Tasks.csproj" | kind=code-symbol | source=Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_engine_tooling_karpik_engine_tooling_csproj": "Karpik.Engine.Tooling.csproj" | kind=code-symbol | source=Karpik.Launcher/Karpik.Launcher.csproj
- "c_users_artem_riderprojects_karpikengine_karpik_launcher_karpik_launcher_csproj": "Karpik.Launcher.csproj" | kind=code-symbol | source=Karpik.Launcher.Tests/Karpik.Launcher.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_modules_client_graphics_graphics_core_graphics_core_csproj": "Graphics.Core.csproj" | kind=code-symbol | source=Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_modules_client_input_input_csproj": "Input.csproj" | kind=code-symbol | source=Modules/Client/Input.Tests/Input.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_modules_client_window_window_core_window_core_csproj": "Window.Core.csproj" | kind=code-symbol | source=Modules/Client/Input.Tests/Input.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_modules_client_window_window_headless_window_headless_csproj": "Window.Headless.csproj" | kind=code-symbol | source=Modules/Client/Input.Tests/Input.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_modules_shared_assetmanagement_assetmanagement_core_assetmanagement_core_csproj": "AssetManagement.Core.csproj" | kind=code-symbol | source=Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_modules_shared_ecs_ecs_core_ecs_core_csproj": "ECS.Core.csproj" | kind=code-symbol | source=Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_templates_karpik_game_source_karpikgame_client_karpikgame_client_csproj": "KarpikGame.Client.csproj" | kind=code-symbol | source=templates/Karpik.Game/Tests/KarpikGame.Tests/KarpikGame.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_templates_karpik_game_source_karpikgame_server_karpikgame_server_csproj": "KarpikGame.Server.csproj" | kind=code-symbol | source=templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj
- "c_users_artem_riderprojects_karpikengine_templates_karpik_game_source_karpikgame_shared_karpikgame_shared_csproj": "KarpikGame.Shared.csproj" | kind=code-symbol | source=templates/Karpik.Game/Tests/KarpikGame.Tests/KarpikGame.Tests.csproj
- "c_users_artem_riderprojects_karpikengine_tools_staticanalyzer_staticanalyzer_csproj": "StaticAnalyzer.csproj" | kind=code-symbol | source=Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj

## Instructions

Write a single JSON object mapping each node id to a one-sentence description
to: C:\Users\artem\RiderProjects\KarpikEngine\.graphify\description-instructions\batch-010.json

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
