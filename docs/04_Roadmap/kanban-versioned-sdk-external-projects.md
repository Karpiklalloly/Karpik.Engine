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
	  - Проверять raw `.slnx`, kind/side, циклы и границы Client/Server/Shared; каждый SDK-проект проверяет фактически вычисленные прямые `ProjectReference`, включая imports.
	  - Результат: foreign `.csproj` и запрещённая imported Client → Server ссылка падают до компиляции с точной диагностикой.

## Engine payload и внешняя игра

- [ ] **Сделать транзакционный packager движка**
	  - Layout: `editor`, `sdk`, `runners`, `modules`, `native`, manifest и `.complete`.
	  - Результат: incomplete/hash-invalid installation никогда не выбирается resolver-ом.
- [ ] **Добавить шаблон отдельной игры**
	  - Создать Client/Server/Shared/Test проекты, `.slnx` и `global.json` без `.karpik` и обязательных Directory.Build файлов.
	  - Результат: игра создаётся в любом каталоге и не содержит относительных ссылок на KarpikEngine.
- [ ] **Доказать обычный CLI workflow вне репозитория**
	  - Запускать `restore`, `build`, `test`, `publish` в уникальном каталоге `%TEMP%`.
	  - Результат: editor и launcher не нужны для сборки.
- [ ] **Перенести runtime bundles во владение игры**
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

%% kanban:settings
```
{"kanban-plugin":"board","list-collapse":[false,false,false,false,false]}
```
%%
