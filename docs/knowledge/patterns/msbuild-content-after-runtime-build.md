# Собирать generated Content после сборки Runtime

Wildcard, вычисленный при evaluation launcher-проекта, не увидит cooked-файлы, которые referenced Runtime-проект создаст позднее. Инкрементальная сборка скрывает проблему: старые файлы уже существуют.

В Static launcher шаблона `_KarpikCollectStaticRuntimeContent` выполняет сбор items после `ResolveProjectReferences`, перед сбором файлов для output и publish. Сначала определяется каталог разрешённой Runtime assembly, затем отдельным glob item перечисляются `Content/manifest.json` и `Content/**/*.cooked`.

Item transform не разворачивает wildcard в список файлов. При построении `TargetPath` использовать квалифицированные metadata именно glob item: `RecursiveDir`, `Filename`, `Extension`. Копирование только manifest/cooked не дублирует исходный Content, уже поступающий через project references.

При материализации через `dotnet new` metadata-фильтр задаётся `WithMetadataValue`: literal XML Condition на metadata может быть обработан как синтаксис шаблона.

Проверять на новой внешней игре с чистыми output/cache: build и publish обоих launcher должны содержать manifest и ожидаемый набор cooked-файлов. Приёмка подтвердила по восемь cooked-файлов для Client и Server.

Связанные материалы:

- [Client launcher template](../../../templates/Karpik.Game/Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj).
- [Server launcher template](../../../templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj).
- [External Static acceptance](../../../Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs).
- [Зафиксированная приёмка](../../../plans/0.6-tail-acceptance-execplan.md).
