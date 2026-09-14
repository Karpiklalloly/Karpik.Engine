# Content Pipeline: практическое использование

> Статус на сентябрь 2026: pipeline собирает `raw-json`, `texture`,
> `font-json` и `shader`, создаёт manifest и cooked-артефакты, генерирует
> `ContentRefs` для JSON и автоматически добавляет cooked output в runtime
> bundle. Внешний проект работает только с установленным NuGet-пакетом SDK.

## Что делает pipeline

Для каждого исходного файла в `Content/` pipeline читает sidecar-файл
`<source>.meta`, валидирует данные, готовит артефакт и публикует результат:

```text
Content/config/player.json
Content/config/player.json.meta
        |
        v
<output>/manifest.json
<output>/artifacts/<hash-prefix>/<hash>.cooked
```

`assetId` — неизменяемая идентичность ассета. Переименование файла и
`logicalName` не требует менять ID. Поддержаны `declaredType: "raw-json"`
для JSON, `texture` для PNG/JPG/JPEG, `font-json` для `.font-json` и
`shader` для `.vert`/`.frag`. Processors проверяют исходные данные и пока
сохраняют исходные bytes без GPU-загрузки или компиляции шейдеров.

## 1. Создать ассет

Положите JSON рядом с будущим `.meta`:

```text
Content/config/player.json
Content/config/player.json.meta
```

`player.json`:

```json
{
  "name": "Player",
  "health": 100
}
```

После создания поддерживаемого исходника проще всего сгенерировать sidecar
командой:

```powershell
dotnet run --project Karpik.Content.Tool -- create `
  --source .\MyGame\Content `
  --file config\player.json `
  --namespace game
```

Она запишет `player.json.meta` рядом с исходником, с новым lowercase GUID,
`logicalName` `game/config/player`, типом `raw-json` и пустыми
`importSettings`/`dependencies`. Команда также принимает `.png`, `.jpg`,
`.jpeg`, `.font-json`, `.vert` и `.frag`. Для font она создаёт суффикс
`.font` в logical name, для shader сохраняет `.vert` или `.frag`, чтобы
несколько входов с одним basename не конфликтовали. Существующий `.meta`
команда не перезаписывает.

Editor при открытии проекта создаёт отсутствующие `.meta` для этих же
расширений с namespace `game`; неизвестные расширения пропускаются.

Файл можно создать и вручную — это полезно при миграции или code review:

`player.json.meta`:

```json
{
  "schemaVersion": 1,
  "assetId": "b7b0fb92-a439-4d3e-9fb6-e5fc2b00b565",
  "declaredType": "raw-json",
  "logicalName": "game/config/player",
  "importSettings": {},
  "dependencies": []
}
```

Требования:

- `assetId` — GUID в формате `D`; после создания его нельзя менять без
  намеренной миграции ссылок;
- `logicalName` имеет вид `namespace/path`, не содержит `..`, обратных
  слешей и пустых сегментов;
- namespace в `logicalName` должен совпадать с аргументом `--namespace`;
- `dependencies` необязателен, но содержит только `assetId` других ассетов;
- `.meta` обязателен для каждого исходного файла.

## 2. Запустить CLI вручную

Из корня checkout:

```powershell
dotnet run --project Karpik.Content.Tool -- build `
  --source .\MyGame\Content `
  --output .\MyGame\obj\Debug\net10.0\Content `
  --namespace game
```

Команды для CI и диагностики:

```powershell
# Только проверить исходники, ничего не публикуя.
dotnet run --project Karpik.Content.Tool -- validate `
  --source .\MyGame\Content --namespace game

# Показать ассеты из manifest.
dotnet run --project Karpik.Content.Tool -- list `
  --manifest .\MyGame\obj\Debug\net10.0\Content\manifest.json

# Показать зависимости одного ассета.
dotnet run --project Karpik.Content.Tool -- why `
  --manifest .\MyGame\obj\Debug\net10.0\Content\manifest.json `
  b7b0fb92-a439-4d3e-9fb6-e5fc2b00b565
```

Коды завершения: `0` — успех, `1` — неверные аргументы, `2` — ошибка
валидации контента, `3` — непредвиденная ошибка.

`build` публикует результат атомарно: при ошибке прежний `manifest.json` и
артефакты остаются рабочими. Одинаковые входные данные дают тот же manifest и
не переписывают совпадающие артефакты.

## 3. Подключить build и генератор к проекту SDK

В каждом runtime `.csproj`, которому нужны ассеты, включите pipeline и, если
`Content` находится не рядом с проектом, укажите его корень:

```xml
<PropertyGroup>
  <KarpikContentEnabled>true</KarpikContentEnabled>
  <KarpikContentNamespace>game</KarpikContentNamespace>
  <KarpikContentSourceRoot>$(MSBuildProjectDirectory)\..\..\Content</KarpikContentSourceRoot>
</PropertyGroup>
```

Шаблон включает это и для `Client`, и для `Server`: оба собирают общий
`Content/` в собственный `obj/.../Content` и кладут cooked-результат в свой
runtime bundle. JSON-схемы префабов, используемые с обеих сторон, должны жить
в `Shared`; `Client` и `Server` не ссылаются друг на друга.

`Karpik.Engine.Sdk` перед компиляцией вызовет packaged `content.dll`,
передаст путь к manifest packaged code generator и добавит все
`**/*.json.meta` как `AdditionalFiles`.

Генератор создаёт в проекте класс `Karpik.Content.Generated.ContentRefs`.
Укажите CLR-тип, в который должен десериализоваться `raw-json`:

```csharp
using Karpik.Content.Runtime;

[ContentType("raw-json")]
public sealed class PlayerConfig
{
    public string Name { get; set; } = string.Empty;
    public int Health { get; set; }
}
```

Для примера выше будет доступна ссылка приблизительно такого вида:

```csharp
using Karpik.Content.Generated;
using Karpik.Content.Runtime;

AssetRef<PlayerConfig> player = ContentRefs.Game_Config_Player;
```

Имена полей образуются из `logicalName`; при конфликте имён генератор добавит
суффикс и выдаст предупреждение. Аннотация выше не создаёт новый processor:
она только связывает уже поддержанный `raw-json` с CLR-типом потребителя.
Для нового `declaredType` всё ещё нужен отдельный processor. `texture`
сейчас только валидирует и публикует encoded image bytes, без runtime-типа.

## 4. Cooked output в runtime bundle

SDK сам копирует cooked `manifest.json` и `artifacts/` из промежуточного
output в `$(TargetDir)\Content` до создания runtime bundle. После build
структура будет такой:

```text
bin/.../Content/manifest.json
bin/.../Content/artifacts/.../*.cooked
karpik-bundle/Content/manifest.json
karpik-bundle/Content/artifacts/.../*.cooked
```

Исходные template-файлы остаются в bundle для старых path-based loaders;
runtime manifest consumers используют cooked данные.

## 5. Загрузить ассет в runtime

При старте приложения зарегистрируйте manifest и file store. Делайте это при
инициализации, а не в hot path кадра:

```csharp
using Karpik.Content.Core;
using Karpik.Content.Generated;
using Karpik.Content.Runtime;

string contentRoot = Path.Combine(AppContext.BaseDirectory, "Content");
ContentManifest manifest = ContentManifest.LoadFromFile(
    Path.Combine(contentRoot, "manifest.json"));

var registry = new ContentRegistry();
registry.RegisterManifest(manifest, new FileContentStore(contentRoot));

await registry.LoadAsync(ContentRefs.Game_Config_Player);
if (registry.TryGet(ContentRefs.Game_Config_Player, out AssetLease<PlayerConfig> lease))
{
    int health = lease.Payload.Health;
}
```

`LoadAsync` выполняет файловый ввод и десериализацию вне lock. В игровом
цикле используйте уже загруженный `AssetRef<T>` через `TryGet`; не вызывайте
`LoadAsync` из `Update`, ECS `Run` или других hot path.

## Текущие ограничения

- Texture processor пока не создаёт `AssetRef<ITexture2D>`, GPU texture или
  import settings; cooked-артефакт содержит исходные encoded PNG/JPEG bytes.
- Font и shader processors валидируют и публикуют source bytes; runtime font
  loader, shader compiler и пользовательские types остаются будущими задачами.
- Старый path-based `AssetManagement.Core` продолжает существовать отдельно;
  не смешивайте его пути с `AssetRef<T>` и manifest pipeline.

См. также [ADR: content pipeline build contract](02_ADR/content-pipeline-build-contract.md).
