---

kanban-plugin: board

---

## Зафиксировано

- [x] **Принять архитектуру внешних игр и версионированного SDK**
	  - ADR: [Versioned engine SDK and external game projects](../02_ADR/versioned-engine-sdk-and-external-game-projects.md).
	  - ExecPlan: [Deliver a versioned SDK and external game-project workflow](../../plans/versioned-sdk-external-projects-execplan.md).
	  - Инварианты: обычные `dotnet` команды; без `.karpik`; каждый `.csproj` использует `Karpik.Engine.Sdk`; один активный проект в editor.

## SDK и валидация

- [x] **Выделить модель игровой solution**
	  - Создать `Karpik.Engine.ProjectModel` и тесты временных `.slnx`/`.csproj`.
	  - Результат: единые `KarpikProjectKind`, `KarpikProjectSide` и диагностики `KARPIK001`–`KARPIK008`.
- [x] **Упаковать `Karpik.Engine.Sdk` как custom MSBuild SDK**
	  - Добавить `Sdk.props`, `Sdk.targets` и task assembly.
	  - Результат: `<Project Sdk="Karpik.Engine.Sdk">` восстанавливается через стандартный SDK resolver; пакет содержит project- и solution-level hooks.
- [x] **Требовать SDK во всех проектах игры**
	  - Проверять raw `.slnx`, kind/side, циклы и границы Client/Server/Shared; разрешены только literal unconditional top-level `ProjectReference`, а evaluated-набор обязан точно совпадать с raw-набором.
	  - Результат: foreign `.csproj`, imported/conditional/target-time graph edge и raw cycle падают до restore walk/reference resolution/компиляции с точной диагностикой.

## Engine payload и внешняя игра

- [x] **Сделать транзакционный packager движка**
	  - Layout: `editor`, `sdk`, `runners`, изолированные `modules/<module-id>`, `native`, manifest и `.complete`.
	  - Результат: incomplete/hash-invalid installation никогда не выбирается resolver-ом.
- [x] **Добавить шаблон отдельной игры**
	  - Создать Client/Server/Shared/Test проекты, `.slnx` и `global.json` без `.karpik` и обязательных Directory.Build файлов.
	  - Результат: игра создаётся в любом каталоге и не содержит относительных ссылок на KarpikEngine.
- [x] **Доказать обычный CLI workflow вне репозитория**
	  - Запускать `restore`, `build`, `test`, `publish` в уникальном каталоге `%TEMP%`.
	  - Результат: editor и launcher не нужны для сборки.
- [x] **Перенести runtime bundles во владение игры**
	  - Runner берётся из Engine SDK, Client/Server bundles — из game build output.
	  - Результат: side-pure bundles запускаются через явный `--bundle`, без fallback на `AppContext.BaseDirectory`.

## Editor и Launcher

- [x] **Ввести `ActiveProjectContext`**
	  - Context владеет installation, build inspection, bundles, watchers, IPC и sessions.
	  - Результат: editor публикует только полностью открытый и проверенный проект.
- [x] **Сделать безопасное переключение одного проекта**
	  - Порядок: cancel build → clients → server → IPC/watchers → workspace → dispose → open candidate.
	  - Результат: старые PID, порты, файлы и snapshots не переходят в новый проект.
- [x] **Добавить стабильный `Karpik.Launcher`**
	  - Recent projects, чтение `global.json`, проверка installation manifest, запуск совместимого editor.
	  - Результат: пользователь открывает `.slnx` через один launcher.
- [x] **Сделать cross-version handoff**
	  - Same-version switch остаётся в editor; incompatible switch возвращается в launcher через handoff и exit code `20`.
	  - Результат: проекты на двух версиях SDK открываются правильными editor binaries.
	  - Проверка: Tooling 40/40, Launcher 12/12, Packager 15/15, Editor 88/88; один внешний editor smoke остаётся opt-in.

## Миграция и приёмка

- [x] **Milestone 8A — собрать runtime из installed engine modules и game bundle**
	  - Runner загружает side-compatible engine modules из проверенного versioned payload и Client/Server+Shared assemblies из game-owned bundle.
	  - Общие runtime assemblies (`Karpik.Engine.Core`, runner, `Dragon`, `Karpik.Jobs`) не дублируются между load contexts.
	  - Результат: `ECSInstaller` регистрирует `EcsDefaultWorld`, server snapshot работает, client-only module не попадает на Server.
	  - Проверка: Runner 98/98, Tooling 41/41, Packager 17/17, Configurator 9/9 + `--validate`, editor resolver 5/5, внешний smoke на свежем layout-v2 installed Runner 1/1; прежняя ошибка `Not found service ... EcsDefaultWorld` отсутствует.
- [x] **Milestone 8B — доказать полноценный внешний runtime**
 	  - Внешняя игра запускает server + два clients, использует ECS и content, отдаёт snapshot и проходит hot reload с непустым state.
 	  - Тест проверяет сохранение ECS state: TotalEntityCount после reload = before + 1 (state restored + новая entity от OnConfigureComplete), GameComponent(42) присутствует в снимках до и после.
 	  - Вывод обоих Client собирается и проверяется на отсутствие Engine crashed.
 	  - Чтение content верифицируется: Content/runtime.txt выводится в лог и проверяется тестом.
 	  - Добавлен switch между двумя проектами: второй game материалзируется, строится, проходит полный multi-worker цикл.
 	  - Native layout консистентен: Packager вырезает native/ префикс, PluginLoadContext ищет native/<rid>/, PATH разделяется Path.PathSeparator.
 	  - После stop/reload не остаются процессы, IPC, shadow/state files и locked files.
- [x] **Milestone 8C — удалить editor-local runtime packaging**
	  - Удалён `Karpik.Editor/EditorRuntimeBundles.targets` (уже не импортировался).
	  - Убраны `SkipEditorRuntimeBundles` из PayloadLayout.cs и Karpik.Editor.Tests.csproj.
	  - RuntimeBundleResolverTests переименован в ProjectRuntimeResolverTests.
	  - Результат: editor использует только runner выбранной installation и bundles активной игры.
- [x] **Milestone 8D — убрать `MyGame` и игровые launcher-ы из engine root**
	  - Перенести полезный sample в game template; удалить `MyGame`, `ClientLauncher`, `ServerLauncher` и их solution/build ссылки.
	  - Результат: внешний шаблон доказал server + два clients, ECS, bundle-owned `Content/`/`Mods/` и hot reload; реальный editor switch доказал teardown; engine build graph и Configurator не содержат game-specific composition roots.
- [ ] **Milestone 8E — завершить модернизацию Configurator и `Network.Codegen`**
	  - Сохранить engine module catalog для SDK payload и вынести оставшиеся исторические `MyGame.*.Main` assumptions из `Network.Codegen` в отдельную будущую работу.
	  - Не менять уже завершённую engine-only генерацию `Generated/KarpikModuleCatalog.props` и `Generated/ModuleLoader.cs` иначе чем через Configurator.
- [ ] **Milestone 8F — пройти полную приёмку и обновить документацию**
	  - Все unit/integration tests, `dotnet build KarpikEngine.slnx -m:1 -nr:false --no-restore`, `git diff --check`, обновление индекса кода.
	  - Desktop smoke: Launcher → project A → server + два clients → switch → project B → compatible editor.
	  - Результат: нет orphan processes, IPC, watchers, locked files или смешанных bundles; ExecPlan можно закрыть.

## Не входит в этот ExecPlan

- [ ] **Универсальный editor для всех исторических версий движка**
	  - Вернуться после стабилизации editor/runtime protocol; текущая модель использует version-matched editor.
- [ ] **Удалённый marketplace SDK и модулей**
	  - Текущий план покрывает NuGet SDK package и локальные/установленные payload providers.
- [ ] **Несколько одновременно активных игр в одном editor**
	  - Один editor владеет одним `ActiveProject`; несколько clients относятся только к этой игре.

## Milestone 5 review hardening

- [x] **Harden external runtime bundle boundaries**
	  - Canonical bounded SDK/runtime proof; post-build Shared content; exact ready cleanup; full ancestor link rejection.
	  - Runner restore/build uses transaction-owned artifacts and leaves repository `bin/obj` unchanged.
	  - Two final external runs passed real empty-state restart hot reload with a new PID, consumed state, shadow cleanup, and clean stop.
- [x] **Close Milestone 5 lifecycle and replacement races**
	  - IPC subscriptions precede sends and are always removed; reload ownership is atomic; killed workers confirm exit before disposal or replacement.
	  - Bundle publication validates existing ancestors before mutation, accepts a proven old primary name during replacement, exposes an evaluated overrideable bundle path, and aligns case-insensitive identity with runtime validation.
	  - Failed pre-load shadow copies are removed; IPC frames are serialized and in-flight requests drain safely; SDK 38/38, Runner 75/75, Configurator 9/9, and three external runtime runs are green.
- [x] **Serialize worker lifecycle transitions**
	  - One transition gate covers start, worker/public reload, stop, and dispose without recursive entry.
	  - Counted stop/dispose intent is atomic with the final process-launch commit, preventing queued starts or in-flight reloads from launching after teardown begins.
	  - Per-worker exit disposition begins before state request, suppresses a proven planned old exit, and restores publication on abort; the production-ordering fixture exits immediately after state response.
	  - Real IPC, callback-reentrancy, launch-intent, and planned-exit races pass 8/8 with Runner 81/81, a fresh external RuntimeBundle restart pass, and a Ready independent re-review; Milestone 6 editor switching is unchanged.

## Milestone 6 foundation

- [x] **Add transactional editor project contexts**
	  - Raw safe `.slnx` validation runs before bounded child-process MSBuild evaluation of exact kind, side, bundle, engine-root, target, and project-reference values.
	  - Candidate contexts remain inactive until publication and carry normalized absolute identity plus a generation token for stale command/output rejection.
	  - Switching blocks commands and concurrent switches, then enforces cancel build → clients → server → services → workspace → context disposal → candidate open → publication.
	  - Review hardening applies the same retry-safe order to shutdown, rejects linked/reparse solution and graph paths before MSBuild, and bounds result JSON through a non-reparse file handle.
	  - TOCTOU hardening retains Windows metadata handles through evaluation; non-Windows uses a bounded metadata-only mirror and rejects unmappable staging paths or explicit non-SDK imports.
	  - Unconfirmed MSBuild termination transfers the process, result path, and input lease/mirror to a capacity-bounded tracked reaper; cleanup occurs only after exit confirmation and can be observed or drained.
- [x] **Wire contexts into editor runtime and desktop UI**
	  - Editor startup, `.slnx` open/switch, build/publish, sessions, snapshots, workspace shutdown, console copy, and UTF-8 process output all use the project-owned runtime path.
	  - Editor 82/82 non-opt-in tests pass; the external two-game process smoke passes 1/1.

%% kanban:settings
```
{"kanban-plugin":"board","list-collapse":[false,false,false,false,false]}
```
%%
