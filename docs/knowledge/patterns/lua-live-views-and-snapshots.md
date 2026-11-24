# Переиспользуемые Lua views и независимые снимки

`pool:get(entity)` возвращает один переиспользуемый live view. Следующий `get/add` через тот же handle переключает его и вложенные views на другую сущность. Сохранённый `local position = view.Position` не закрепляет исходную сущность и не копирует значения. Live access требует активный callback владельца.

Для независимых данных использовать `pool:copy(entity)`. Это снимок одного компонента, включая nested/fixed данные, а не клонирование ECS-сущности. Изменение или удаление source не меняет снимок; изменение снимка само не записывается в ECS. Снимок доступен между callbacks до завершения своей access revision; при hot reload старый lifetime gate закрывается до завершения самой Simulation.

Для повторного копирования создать `pool:new_snapshot()` один раз и использовать `pool:copy_to(entity, buffer)`. Root и вложенные views буфера сохраняют identity, данные перезаписываются. Проверять совместимость компонента и Simulation до изменения буфера. Создание снимка аллоцирует; common C# copy_to после подготовки должен давать 0 B. Lua VM остаётся отдельным источником аллокаций.

Снимок не должен удерживать source accessor, pool или world: копировать значения и безопасную metadata, оставлять только lifetime gate владельца (access revision/Simulation). Проверять независимость после get другой сущности, удаления source и роста storage, стабильность вложенных views при copy_to, disposal guards и освобождение source world через WeakReference.

[API](../../modules/modding/typed-bindings.md) · [Common snapshot tests](../../../tests/Karpik.Engine.Core.Runner.Tests/ComponentSnapshotTests.cs) · [Lua tests](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaCompositeBindingsTests.cs)

[Владение ECS storage и code revisions](ecs-storage-and-code-revisions.md)
