# Пиковая ёмкость структурной очереди

Итоговое количество компонентов не доказывает, что очередь выполнится без роста памяти. Preflight должен последовательно моделировать FIFO membership и проверять ёмкость после каждой команды.

Например, native pool с Capacity=8 хранит максимум семь компонентов: нулевой элемент служебный. При Count=7 очередь `add → del` имеет нулевое итоговое изменение, но первая команда потребует восьмой компонент. Отклонять всю очередь до применения. У dynamic dense pool служебного нулевого элемента нет; его предел проверяется отдельно.

Начальное состояние брать после callback: немедленные операции уже могли изменить мир. Учитывать повторные команды, поколения целей, native/dynamic pools и технический anchor. Для запрета удаления последнего компонента считать все логические компоненты сущности, включая не объявленные в query; DynamicComponentAnchor сам по себе логическим компонентом не является.

Буфер команд, payload, validation scratch, мир и component pools имеют отдельные бюджеты. Подготовленная очередь сама по себе не гарантирует применение без аллокаций. Проверять точную границу и превышение на один, отсутствие частичных изменений при поздней ошибке и warmed allocation всего common enqueue/preflight/playback. Lua VM и произвольные native listeners требуют отдельных измерений.

[Реализация](../../../Modules/Shared/Modding/Modding.Core/Bindings/StructuralCommandBuffer.cs) · [Peak/FIFO тесты](../../../tests/Karpik.Engine.Core.Runner.Tests/StructuralCommandBufferTests.cs) · [Common probe](../../../plans/probes/lua-structural-commands/README.md) · [Учёт ID мира](ecs-deleted-id-capacity.md)
