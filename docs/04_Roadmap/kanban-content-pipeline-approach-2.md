---

kanban-plugin: board

---

## Content Pipeline — подход 2

Цель: воспроизводимый build-time pipeline и runtime registry ресурсов на стабильных ID. Runtime не знает исходных путей; он читает manifest и cooked artifacts. Архитектура должна позволять позднее заменить отдельные файлы pack/chunk storage без изменения `AssetRef<T>` и потребителей.

## Сначала: контракт и формат

- [ ] **Зафиксировать ADR: идентичность, владение и границы подсистем**
	  - Что сделать: определить `AssetId` как 128-bit GUID из `.meta`, `AssetRef<T>` как копируемую типизированную ссылку без владения и `AssetLease<T>` как единственный явный способ удерживать loaded payload.
	  - Зачем: строковый путь и копируемый ref-count handle не дают безопасного владения и ломают переименование/упаковку.
	  - Результат: `Shared` содержит только ID, manifest и runtime-нейтральные контракты; Veldrid/GPU типы остаются в `Client`.
- [ ] **Определить schema build manifest и cooked artifact**
	  - Что сделать: описать versioned manifest entry: `AssetId`, type, logical name/namespace, import settings hash, source hash, cooked artifact locator, размер и прямые зависимости.
	  - Зачем: manifest должен быть источником истины для build, validation, runtime и hot reload.
	  - Результат: locator остаётся непрозрачным для потребителей — это задел для future pack/chunk storage.
- [ ] **Определить namespace и override-правила модов**
	  - Что сделать: ввести identity мода, namespace ресурсов, явную декларацию override и детерминированный порядок разрешения конфликтов.
	  - Зачем: два мода не должны молча подменять один и тот же ресурс по одинаковому пути.
	  - Результат: конфликт manifest'ов диагностируется до запуска; runtime получает уже разрешённый effective manifest.

## Build pipeline

- [ ] **Создать CLI-проект content build и команды диагностики**
	  - Что сделать: добавить headless команды `build`, `validate`, `list` и `why <AssetId>`; команды принимают source root, output root и набор manifest'ов модов.
	  - Зачем: content должен собираться в CI и на build-машине без GUI/editor.
	  - Результат: Debug и Release используют один cooked output, а ошибки формата/зависимостей видны до запуска игры.
- [ ] **Добавить `.meta` и реестр стабильных ID**
	  - Что сделать: создать и валидировать sidecar metadata с GUID, declared type и import settings; запрещать дубли ID и несовместимое изменение типа.
	  - Зачем: перемещение или переименование source-файла не должно менять ссылки из prefab, scene и tilemap.
	  - Результат: все entries manifest имеют стабильный ID, а путь остаётся только authoring-метаданными.
- [ ] **Реализовать processors первого среза**
	  - Что сделать: processors для raw data/JSON, textures, font atlas+metrics и shader artifacts; каждый сообщает входные файлы, output и зависимости.
	  - Зачем: это покрывает существующий набор `AssetManagement` и переводит его на build-time проверку.
	  - Результат: runtime не декодирует исходные форматы и не ищет loaders по расширению.
- [ ] **Построить dependency graph и incremental rebuild**
	  - Что сделать: строить прямой и обратный граф из processor output; по source/meta изменению пересобирать только changed asset и его dependents.
	  - Зачем: hot reload должен быть адресным, а не перезагружать весь content.
	  - Результат: `why` объясняет путь зависимости; циклы и missing dependencies завершают validation с ошибкой.

## Runtime registry

- [ ] **Ввести manifest reader и `IContentStore`**
	  - Что сделать: runtime читает effective manifest через `IContentStore`, который открывает artifact по opaque locator; первая реализация использует отдельные cooked-файлы.
	  - Зачем: `IContentStore` изолирует файловую систему и позволит позже подставить pack/mmap/chunk storage.
	  - Результат: gameplay и rendering не вызывают `File.Open`, не знают `Content/`/`Mods/` и не используют source paths.
- [ ] **Заменить текущие `Asset`/`AssetHandle<T>` на slot-based registry**
	  - Что сделать: хранить payload, state, version и dependency metadata в registry slots; выдавать `AssetRef<T>` для долгоживущих ссылок и explicit `AssetLease<T>` для временного использования.
	  - Зачем: устранить копирование ref-count struct, коллизии hash-path и гонки параллельной загрузки.
	  - Результат: single-flight load на slot, типовая проверка и понятная ошибка missing/failed asset.
- [ ] **Разделить CPU loading и backend upload**
	  - Что сделать: I/O, decompression и CPU decode выполнять вне frame loop; GPU create/upload/dispose выполнять в явной backend-owned фазе Client graphics lifecycle.
	  - Зачем: Veldrid-ресурсы не должны создаваться на произвольном worker, а `Update`/ECS/merge не должны ждать I/O.
	  - Результат: нет managed allocations, блокировок и file I/O в steady-state hot paths; thread affinity проверяема тестом/интеграционным сценарием.
- [ ] **Мигрировать texture, shader и font потребителей**
	  - Что сделать: перевести `TextureLoader`, `ShaderLoader`, `FontLoader`, `Preset2DPipeline` и sample client с path-based загрузки на `AssetRef<T>` и registry.
	  - Зачем: доказать вертикальный срез до подключения tilemaps, prefab и editor tooling.
	  - Результат: 2D sample запускается из cooked content; исходный `AssetsManager` больше не участвует в graphics runtime path.

## Hot reload, диагностика и качество

- [ ] **Сделать versioned hot reload с безопасной публикацией**
	  - Что сделать: watcher/build notification определяет изменённые IDs, пересобирает reverse dependencies и публикует новые slot versions в safe phase; старый GPU payload освобождается после завершения использующего его кадра.
	  - Зачем: нельзя заменить или dispose texture/resource set, пока merge/submission ещё может на него ссылаться.
	  - Результат: repeated reload не оставляет stale payload, event subscription или GPU resource; ECS gameplay state не затрагивается.
- [ ] **Добавить content diagnostics и минимальный ImGui Asset Inspector**
	  - Что сделать: показать ID, logical name, type, state, version, размер, зависимости, processor errors и reload result; editor не входит в этот срез.
	  - Зачем: сначала нужно наблюдать и отлаживать pipeline, а не дублировать его GUI-логикой.
	  - Результат: developer может найти missing/failed asset и объяснить его зависимости без просмотра файлов вручную.
- [ ] **Покрыть контракт тестами и CI validation**
	  - Что сделать: unit tests для ID/meta/manifest, graph/cycle/override tests, processor golden tests, registry ownership/race tests, repeated hot-reload integration test и allocation checks для render/ECS путей.
	  - Зачем: ошибки владения, модовых override и lifecycle проявляются не только при первом запуске.
	  - Результат: CI запускает content validation и targeted tests; acceptance включает запуск 2D sample и несколько последовательных reload.

## После измерений: подход 3 (не реализовывать в этом срезе)

- [ ] **Заменить файловый `IContentStore` на pack/chunk storage**
	  - Начинать только при измеренной проблеме startup/IO/memory или при требовании patch/streaming.
	  - Граница: public `AssetRef<T>`, manifest identity и registry slots не меняются; меняется лишь internal locator resolution и cache policy.
- [ ] **Добавить streaming, budgets и eviction**
	  - Начинать только с workload-метриками: residency, IO latency, peak memory и eviction churn.
	  - Граница: загрузочные приоритеты и LRU не попадают в ECS/render hot paths.

%% kanban:settings
```
{"kanban-plugin":"board","list-collapse":[null]}
```
%%
