# Паритет optional DI в Static и Dynamic

Добавление optional constructor dependency не должно превращать отсутствующий сервис в обязательный только в Static composition. Если параметр имеет явный default null, сгенерированная фабрика использует зарегистрированный сервис при наличии и null при отсутствии. Required dependencies продолжают разрешаться строго.

Default нужно учитывать и в модели/equality incremental generator: изменение optional-признака при прежнем типе параметра должно обновлять фабрику. Разрешение остаётся частью constructor-injection factory; переносить lookup в runtime-сервис или hot path не требуется.

Проверять реальное создание и подготовку сервисов в четырёх сочетаниях: Static/Dynamic × dependency зарегистрирована/отсутствует. Проверка только текста generated source не выявляет runtime DependencyResolutionException. В Lua preparation добавление optional MainThreadScheduler выявило именно такой разрыв; отсутствие сервиса и его регистрация теперь проверяются в composed scopes.

[Генератор фабрик](../../../src/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs) · [Composed-scope тест](../../../tests/Karpik.Engine.Core.Generator.Tests/ClrComponentBindingGeneratorTests.cs) · [Зафиксированная проверка](../../../plans/lua-hot-reload-evidence.md)
