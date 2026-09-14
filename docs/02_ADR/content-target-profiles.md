---
title: "Target-aware профили сборки контента"
date: "2026-09-14"
status: "accepted"
tags:
  - adr
  - architecture
  - content
---

# Target-aware профили сборки контента

> Статус: принят
> Дата: 2026-09-14
> Владельцы: разработчик и AI assistant
> Связанный документ: [[content-pipeline-build-contract]]

## Контекст

Сейчас шаблон игры независимо готовит всё дерево `Content/` для Client и
Server. Благодаря этому оба runtime bundle самодостаточны, но Server manifest
может содержать Client-only ассеты: текстуры, шейдеры или аудио. Имена папок
полезны как соглашение для авторинга, но не могут быть контрактом доставки:
перенос файла не должен неявно менять состав его bundle.

Pipeline также нужна безопасная основа для будущих Client- и
Server-вариантов, специфичных для processor. Например, будущий processor
текстур сможет использовать разные настройки для каждой стороны. Текущий
формат результата уже состоит из manifest и content-addressed artifacts;
потребности в монолитном файле `content.pack` пока нет.

## Решение

`Karpik.Content.Core` определяет flags-enum `AssetTarget` со значениями
`Client`, `Server` и `Shared = Client | Server`. Каждый sidecar ассета получает
необязательный канонический массив `targets`. Отсутствующее поле означает
`Shared` для обратной совместимости; Editor и `content create` записывают оба
target для новых sidecar.

SDK запускает CLI со своим runtime `KarpikSide`. CLI предоставляет тот же
выбор через `--target Client|Server`, а Core получает его в неизменяемом
профиле content build. Сборка target включает ассет, только если его `targets`
содержит выбранный target. Результат записывается в существующем формате
`manifest.json` и `artifacts/` в обычный output соответствующего runtime;
`content.pack` это решение не добавляет.

Профиль сборки передаётся processor через `ContentProcessorContext`. Текущие
processor создают одинаковые bytes для обоих target, но выбранный target всё
равно включается в канонический recipe hash и идентичность manifest. Поэтому
будущие processor смогут создавать target-specific artifacts без конфликтов
кеша и переработки API.

Валидация учитывает target:

- `assetId` остаётся глобально уникальным во всём дереве исходников;
- `logicalName` должен быть уникальным только среди ассетов, выбранных в один
  manifest target. Поэтому независимые Client-only и Server-only ассеты могут
  иметь одинаковый logical name;
- прямая зависимость выбранного ассета тоже должна быть выбрана для этого
  target;
- ассет, доступный обоим target, может зависеть только от ассета, доступного
  обоим target. Client-only и Server-only ассеты могут зависеть от своего
  target или общего ассета.

`Content/Client`, `Content/Server` и `Content/Shared` остаются рекомендуемыми
папками для авторинга, но не особыми build root. Единственный источник истины
— `targets` в sidecar.

## Последствия

Client и Server bundle содержат отдельные manifest и отдельные content hash,
даже если текущие cooked bytes совпадают. Это осознанно создаёт небольшое
дублирование при сборке и хранении, зато каждый bundle разворачивается
независимо и получает стабильную точку расширения для будущих вариантов.

Исходник ассета, `assetId` и `logicalName` остаются стабильными между target.
Игровое определение, которому нужны разные authoritative и presentation
данные, обычно моделируется отдельными Server и Client ассетами с общим
игровым идентификатором, а не одним непрозрачным JSON, чья интерпретация
зависит от стороны.

Не меняются runtime loader, ECS system, renderer, pack archive и hot-path
код. Выбор target, проверка зависимостей и cooking выполняются при работе
Editor, CLI и MSBuild.

## Рассмотренные альтернативы

### Include-glob папок в project files

Отклонено. Такой вариант делает доставку зависимой от расположения в файловой
системе и дублирует политику выбора между Client и Server project files.

### Один общий ассет с разной компиляцией без target в идентичности

Отклонено. Он допускает конфликты кеша и делает `assetId` неоднозначным.
Target должен стать частью recipe до того, как processor получит такое
поведение.

### Добавить `content.pack` сейчас

Отклонено для текущего среза. Существующий контракт manifest/artifact уже
поставляет target-specific output. Pack будет оправдан позже измеренными
требованиями к старту, streaming, signing или доставке.

## Валидация

- Unit-test parsing отсутствующего, Client-only, Server-only и Shared
  `targets`.
- Собрать каждый target из одного дерева исходников и проверить, что его
  manifest содержит только выбранные ассеты.
- Проверить разрешённые и запрещённые рёбра зависимостей для обеих сборок.
- Проверить, что одинаковый logical name у независимых target допустим, а у
  пересекающихся — вызывает ошибку.
- Проверить, что artifact locator различается по target даже при одинаковых
  bytes processor output.
- Проверить, что SDK передаёт `KarpikSide` packaged CLI, а каждый runtime
  bundle шаблона содержит собственный cooked manifest.
