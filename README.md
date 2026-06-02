# KarpikEngine

**🌐 Language:** [🇺🇸 English](README-ENG.md) | 🇷🇺 Русский

> 2D-first игровой движок на C# с ECS-архитектурой, горячей перезагрузкой и разделением Client / Server / Shared

KarpikEngine — экспериментальный open-source движок для разработки 2D-игр. Основные приоритеты: data-oriented архитектура, отсутствие аллокаций в hot paths, предсказуемый lifecycle и возможность начать с single-player логики без блокировки дальнейшего перехода к мультиплееру.

Актуальный релиз: **v0.4**. Подробности: [Changelog_0.4.md](Changelog_0.4.md).

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
- Безопасный scheduler для параллельного выполнения пользовательских ECS-систем запланирован на `v0.5`

## 🚀 Быстрый старт

### Требования
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) или выше

### Установка и запуск
1. Клонируйте репозиторий:
   ```bash
   git clone https://github.com/your-username/KarpikEngine.git
   cd KarpikEngine
   ```

2. Соберите launchers:
   ```bash
   dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false
   dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false
   ```

3. Запустите сервер:
   ```bash
   dotnet run --project ServerLauncher/ServerLauncher.csproj
   ```

4. В другом терминале запустите клиент:
   ```bash
   dotnet run --project ClientLauncher/ClientLauncher.csproj
   ```

### Горячая перезагрузка
1. Измените код и соберите соответствующий launcher.
2. Нажмите `Hot Reload` в клиентской debug-панели или клавишу `R` в консоли launcher.
3. Новый worker-процесс восстановит ECS-состояние.

В Debug-режиме включите автоматическое подключение IDE debugger к дочерним процессам, если хотите отлаживать worker после рестарта.

> Храните состояние, которое должно пережить Hot Reload, в ECS-компонентах. Runtime-ресурсы и process-local handles должны пересоздаваться.

## 🗺️ Состояние проекта

### ✅ Реализовано в v0.4
- ECS core, world-фасады и engine-owned lifecycle систем
- Client / Server / Shared границы и Configurator validation
- Restart-worker Hot Reload с восстановлением ECS-миров
- OpenGL 2D renderer, SDL2 window/input, batching, камера и текст
- AssetManagement, Tween, Lua-моддинг и Dependency Injection
- Physics2D API, Aether2D backend и platformer sample
- Тесты lifecycle, ECS component lifecycle и графа модулей

### 🔮 Следующие направления
- `v0.5`: scheduler, стабилизация `Karpik.Jobs`, no-GC scheduling и memory allocators
- Развитие 2D renderer, asset pipeline, input и audio API
- Новый UI API вместо удалённого prototype UI Toolkit
- Инструменты разработчика, профилирование и расширение сетевого sample

Подробнее: [Roadmap 1.0](docs/04_Roadmap/karpikengine-1.0-roadmap.md).

## 🏗️ Архитектура проекта

Проект разделён на переиспользуемые **модули** и игровые проекты **MyGame**. Обе группы делятся на Client, Server и Shared части.

### Основные каталоги
- `Modules/Client` — rendering, input и client-side presentation
- `Modules/Server` — серверная логика и validation
- `Modules/Shared` — общая логика без зависимости от runtime side
- `MyGame` — sample game с клиентской, серверной и общей частями
- `Configurator` — validation и генерация графа модулей

### Добавление зависимости
В проектах внутри `Modules` и `MyGame` используйте `KarpikModuleDependency`, а не прямой `ProjectReference`:

```xml
<KarpikModuleDependency Include="Physics2D" />
```

После добавления, удаления или перемещения проекта выполните:

```bash
dotnet run --project Configurator/Configurator.csproj -- --generate
dotnet run --project Configurator/Configurator.csproj -- --validate
```

Добавление существующего идентификатора зависимости требует только reload проекта или сборки.

## 🤝 Участие в разработке

KarpikEngine — open-source проект. Issue и Pull Request должны учитывать real-time ограничения: отсутствие аллокаций в hot paths, cache locality, фиксированный dt для симуляции и границы Client / Server / Shared.

## 💬 Сообщество

- 💬 **Discord:** [https://discord.gg/UvdEuY2D2V](https://discord.gg/UvdEuY2D2V)
- 🐛 **GitHub Issues** — баги и предложения
- 📖 **GitHub Discussions** — общие вопросы

## 📄 Лицензия

Проект распространяется под лицензией MIT. Подробности в файле [LICENSE](LICENSE).
