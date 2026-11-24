# Точность чисел на границе Lua и CLR

Lua number уже содержит binary double. Конструктор точного типа может сохранить только это представленное значение: цифры, потерянные при разборе Lua literal, восстановить нельзя. Для точных i64/u64/decimal значений использовать строковый конструктор; не проводить их через общий double или object-хранилище.

Проверка `double → decimal → double` недостаточна: округлённый decimal может вернуться к тому же double. Проверять точную рациональную представимость исходного IEEE-754 значения. Binary double `0.1` не представим точно как decimal, поэтому `types.decimal(0.1)` отклоняется, а `types.decimal("0.1")` сохраняет нужное значение.

До записи целого проверять тип, конечность, целочисленность и диапазон. Явное преобразование decimal в integer не должно отбрасывать дробь. Переполнение вызывает ошибку; arithmetic между разными exact kinds требует явного преобразования. Сохранённое exact Lua значение неизменно: следующий getter или результат операции не перенаправляет старый wrapper.

Проверять extrema, значения около 2^53, дроби и nonfinite, invariant string round-trip и сравнение с CLR arithmetic. Парсинг/форматирование и immutable Lua wrappers измерять отдельно от общего C# доступа без аллокаций.

[Контракт](../../modules/modding/typed-bindings.md) · [Реализация](../../../Modules/Shared/Modding/Modding.Lua/LuaExactValues.cs) · [Числовые тесты](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaExactValuesTests.cs)
