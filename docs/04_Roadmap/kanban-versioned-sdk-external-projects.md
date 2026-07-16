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

- [ ] **Ввести `ActiveProjectContext`**
	  - Context владеет installation, build inspection, bundles, watchers, IPC и sessions.
	  - Результат: editor публикует только полностью открытый и проверенный проект.
- [ ] **Сделать безопасное переключение одного проекта**
	  - Порядок: cancel build → clients → server → IPC/watchers → workspace → dispose → open candidate.
	  - Результат: старые PID, порты, файлы и snapshots не переходят в новый проект.
- [ ] **Добавить стабильный `Karpik.Launcher`**
	  - Recent projects, чтение `global.json`, проверка installation manifest, запуск совместимого editor.
	  - Результат: пользователь открывает `.slnx` через один launcher.
- [ ] **Сделать cross-version handoff**
	  - Same-version switch остаётся в editor; incompatible switch возвращается в launcher через handoff и exit code `20`.
	  - Результат: проекты на двух версиях SDK открываются правильными editor binaries.

## Миграция и приёмка

- [ ] **Удалить editor-local runtime packaging**
	  - Удалить `Karpik.Editor/EditorRuntimeBundles.targets` после прохождения external runtime smoke.
- [ ] **Убрать `MyGame` и игровые launcher-ы из engine root**
	  - Перенести полезный sample в game template; удалить `MyGame`, `ClientLauncher`, `ServerLauncher` и их solution/build ссылки.
	  - Результат: engine build graph не содержит game-specific composition roots.
- [ ] **Отвязать Configurator от `MyGame` и root profile игры**
	  - Оставить генерацию engine module catalog; game profile обрабатывает `Karpik.Engine.Sdk`.
	  - Результат: engine и game graphs валидируются независимо.
- [ ] **Пройти полную автоматическую приёмку**
	  - Все unit/integration tests, `dotnet build KarpikEngine.slnx -m:1 -nr:false --no-restore`, `git diff --check`, `graphify update .`.
- [ ] **Пройти desktop smoke для двух SDK**
	  - Launcher → project A → server + два clients → switch → project B → compatible editor.
	  - Результат: нет orphan processes, IPC, watchers, locked files или смешанных bundles.

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
- [ ] **Wire contexts into editor runtime and desktop UI**
	  - Replace editor-local bundle resolution, connect sessions/watchers/workspace lifetime, add asynchronous `.slnx` open/switch, and prove the external two-game process smoke.

%% kanban:settings
```
{"kanban-plugin":"board","list-collapse":[false,false,false,false,false]}
```
%%
