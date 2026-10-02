# KarpikEngine

**🌐 Language:** [🇺🇸 English](README-ENG.md) | 🇷🇺 Русский

> 2D-first игровой движок на C# с ECS-архитектурой, горячей перезагрузкой и разделением Client / Server / Shared

KarpikEngine — экспериментальный open-source движок для разработки 2D-игр. Основные приоритеты: data-oriented архитектура, отсутствие аллокаций в hot paths, предсказуемый lifecycle и возможность начать с single-player логики без блокировки дальнейшего перехода к мультиплееру.

Последний changelog: **v0.6** — [Changelog_0.6.md](Changelog_0.6.md) ([на русском](Changelog_0.6_ru.md)).

## ✨ Ключевые особенности

### 🏗️ ECS и lifecycle
- Используется [Dragon ECS](https://github.com/DCFApixels/DragonECS) от DCFApixels
- Gameplay-состояние хранится в ECS `struct`-компонентах
- Движок задаёт предсказуемый pipeline: `Init -> Begin -> FixedUpdate -> Update -> LateUpdate -> Render -> Destroy`
- Для пользовательского кода добавлены фасады `DefaultWorld`, `EventWorld` и `MetaWorld`
- Physics и gameplay simulation выполняются с фиксированным dt

### 🔥 Горячая перезагрузка
- Hot Reload работает через restart-worker модель без ограничений стандартного .NET Hot Reload
- Между перезагрузками сохраняются ECS-миры
- Сервисы, графические ресурсы, сокеты и process-local handles пересоздаются в новом worker-процессе
- Клиент и сервер можно перезагружать независимо

### 🌐 Client / Server / Shared
- Клиентская, серверная и общая логика разделены на уровне проектов
- Configurator проверяет недопустимые зависимости до запуска приложения
- Добавлены RPC и базовый сетевой sample с переподключением после Hot Reload

### 📦 Модульная архитектура
- Модули имеют независимый lifecycle и подключаются через интерфейсы
- Зависимости задаются короткими идентификаторами `KarpikModuleDependency`
- Configurator валидирует граф модулей, циклы, side leaks и generated-файлы
- Rider и компилятор получают обычные `ProjectReference` через generated-каталог

### 🎨 2D runtime
- Добавлены OpenGL renderer, SDL2 window/input backend и command-buffer API
- Поддерживаются прямоугольники, текстуры, атласные SDF-шрифты, batching и `Camera2D`
- Добавлены ImGui overlay, AssetManagement, Tween и Lua-моддинг
- Добавлены Physics2D API, backend `Physics2D.Aether2D` и platformer sample

### ⚡ Производительность
- Runtime проектируется без аллокаций после warm-up в frame, fixed-update, render и network hot paths
- `Karpik.Jobs` уже используется внутри движка
- Добавлены scheduler ECS-систем, no-GC value jobs и unmanaged memory primitives в `v0.5`

## 🚀 Быстрый старт

### Требования
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) или выше

### Установка и запуск (разработка движка)
1. Клонируйте репозиторий:
   ```bash
   git clone https://github.com/Karpiklalloly/Karpik.Engine.git KarpikEngine
   cd KarpikEngine
   ```

2. На Windows опубликуйте локальный SDK:
   ```powershell
   ./_scripts/Update-KarpikSdk.ps1
   ```
   Скрипт выводит точную версию SDK, публикует immutable engine payload и регистрирует локальный NuGet feed.

3. Создайте игру вне репозитория движка — из лаунчера или шаблоном, указав выведенную версию:
   ```powershell
   dotnet new karpik-game --name MyGame --output ../MyGame --karpik-sdk-version <sdk-version>
   cd ../MyGame
   dotnet build MyGame.slnx -m:1 -nr:false
   ```
   Шаблонов два: `karpik-game` (минимальная игра) и `karpik-coinrush` (рабочий мультиплеерный семпл CoinRush).

### Установка из дистрибутива (разработка игры, без исходников движка)
Возьмите два каталога нужной версии (`<версия>-sdk` и `<версия>-editor-launcher`) и выполните по порядку — требуется .NET 10 SDK:
```powershell
sdk\setup.exe sdk --payload sdk\sdk-payload.zip
editor-launcher\setup.exe editor --payload editor-launcher\editor-payload.zip
editor-launcher\setup.exe launcher --source editor-launcher\launcher-files
```
Дальше — запустите лаунчер из меню «Пуск» и создайте игру из шаблона. Сборка дистрибутива из исходников: `./_scripts/New-KarpikDistribution.ps1 -SdkVersion "0.6.0" -EngineVersion "0.6.0"`.

4. Запустите Static launcher сервера и затем клиента в другом терминале:
   ```powershell
   dotnet run --project Source/MyGame.Server.Launcher/MyGame.Server.Launcher.csproj --no-build
   dotnet run --project Source/MyGame.Client.Launcher/MyGame.Client.Launcher.csproj --no-build
   ```
   Для desktop workflow запустите `Karpik.Launcher` из engine checkout и откройте `.slnx` игры. Launcher выбирает editor по версии SDK в `global.json`. Режимы Static/Dynamic описаны в [шаблоне игры](templates/Karpik.Game/README.md).

### Пересборка и перезапуск
1. Остановите сессии игры в editor.
2. Измените код и соберите соответствующий launcher.
3. Перезапустите сервер и добавьте клиентов в editor. Обычный stop/start создаёт новое ECS-состояние.

В Debug-режиме включите автоматическое подключение IDE debugger к дочерним процессам, если хотите отлаживать worker после рестарта.

> Храните состояние, которое должно пережить Hot Reload, в ECS-компонентах. Runtime-ресурсы и process-local handles должны пересоздаваться.

## 🗺️ Состояние проекта

### ✅ Реализовано в v0.5
- No-GC value jobs (`IJob`/`IJobFor`, `JobScheduler`), unmanaged memory primitives (`Karpik.Memory`)
- Параллельный планировщик ECS `ISystemUpdate` со статической кодогенерацией и Roslyn-валидацией
- Threaded client pipeline: симуляция в отдельном потоке, triple-buffered render-команды

### ✅ Реализовано в v0.6
- Статическая композиция рантайма по умолчанию, NativeAOT-публикация static-хостов
- Внешний версионированный MSBuild SDK, шаблоны `karpik-game` и `karpik-coinrush`
- DI через конструкторы (`[Export]` + `[ServiceRegistration]`, скопы Engine/ModSet/Simulation)
- Контент-пайплайн (`AssetRef`/`Lease`, `ContentRegistry`, кодогенерация `ContentRefs`)
- Типизированный реестр сетевых снапшотов с хешем схемы протокола
- Unity-like редактор, диагностика на `ILogger`, exe-дистрибуция (SDK и Launcher+Editor бандлы)

### ✅ Реализовано в v0.4
- ECS core, world-фасады и engine-owned lifecycle систем
- Client / Server / Shared границы и Configurator validation
- Restart-worker Hot Reload с восстановлением ECS-миров
- OpenGL 2D renderer, SDL2 window/input, batching, камера и текст
- AssetManagement, Tween, Lua-моддинг и Dependency Injection
- Physics2D API, Aether2D backend и platformer sample
- Тесты lifecycle, ECS component lifecycle и графа модулей

### 🔮 Следующие направления
- Развитие 2D renderer, asset pipeline, input и audio API
- Новый UI API вместо удалённого prototype UI Toolkit
- Инструменты разработчика, профилирование и расширение сетевого sample

Подробнее: [Roadmap 1.0](docs/04_Roadmap/karpikengine-1.0-roadmap.md).

## 🏗️ Архитектура проекта

Репозиторий содержит движок, переиспользуемые модули и инструменты. Игры создаются из шаблона и живут в отдельных каталогах; их Client, Server и Shared проекты используют `Karpik.Engine.Sdk`.

### Основные каталоги
- `Modules/Client` — rendering, input и client-side presentation
- `Modules/Server` — серверная логика и validation
- `Modules/Shared` — общая логика без зависимости от runtime side
- `templates/Karpik.Game` — минимальный шаблон внешней игры со Static launcher-ами и контентом
- `templates/Karpik.CoinRush` — шаблон мультиплеерного семпла CoinRush
- `Karpik.Editor`, `Karpik.Launcher` — desktop workspace и выбор версии editor
- `Karpik.Engine.Sdk`, `Karpik.Engine.Packager` — NuGet SDK и публикация engine payload
- `Karpik.Engine.Setup`, `_scripts/New-KarpikDistribution.ps1` — exe-установщик и сборка дистрибутива (SDK и Launcher+Editor бандлы)
- `Configurator` — validation и генерация графа модулей

### Добавление зависимости
В проектах движка внутри `Modules` используйте `KarpikModuleDependency`:

```xml
<KarpikModuleDependency Include="Physics2D" />
```

После добавления, удаления или перемещения проекта выполните:

```bash
dotnet run --project Configurator/Configurator.csproj -- --generate
dotnet run --project Configurator/Configurator.csproj -- --validate
```

Добавление существующего идентификатора зависимости требует только reload проекта или сборки.

Во внешней игре выбирайте модули через `KarpikModuleSelection` в `Directory.Build.targets`; зависимости между игровыми проектами задавайте обычным literal `ProjectReference`. Configurator запускается только для графа движка. SDK проверяет граф игры, границы сторон и создаёт runtime bundles в output самой игры.

## 🤝 Участие в разработке

KarpikEngine — open-source проект. Issue и Pull Request должны учитывать real-time ограничения: отсутствие аллокаций в hot paths, cache locality, фиксированный dt для симуляции и границы Client / Server / Shared.

## 💬 Сообщество

- 💬 **Discord:** [https://discord.gg/UvdEuY2D2V](https://discord.gg/UvdEuY2D2V)
- 🐛 **GitHub Issues** — баги и предложения
- 📖 **GitHub Discussions** — общие вопросы

## 📄 Лицензия

Проект распространяется под лицензией MIT. Подробности в файле [LICENSE](LICENSE).
