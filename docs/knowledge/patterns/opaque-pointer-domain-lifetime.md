# Время жизни непрозрачных pointer tokens

Pointer token не владеет памятью и не продлевает жизнь pointee. Даже корректная проверка token не доказывает, что произвольный native адрес жив: за это отвечает доверенный C# host.

Host заранее регистрирует разрешённые pointer type IDs и lifetime domains в Simulation-scoped ComponentPointerRegistry. Перед освобождением native памяти отзывать domain через Revoke. После отзыва отклонять сохранённые non-null tokens, включая находящиеся в snapshots. Копирование token не копирует pointee и не отменяет revocation.

Lua получает только nil/null либо opaque value. Не предоставлять raw address, числовой конструктор, arithmetic или dereference. Перед обратной записью проверять registry/Simulation, type и domain generation; T* и void* не преобразуются автоматически. Null можно использовать для очистки поля отозванного типа, пока registry жива.

Передавать policy через constructor injection; зарегистрированный сервис не создавать скрытым fallback. Legacy scalar-only вызовы могут обходиться без policy, pointer bindings без неё должны отклоняться. В горячем пути использовать prepared type handles и плотные таблицы проверки, без строкового поиска и аллокаций. Native refs/pointers из accessor не сохранять между вызовами.

[API](../../modules/modding/typed-bindings.md) · [Registry](../../../Modules/Shared/Modding/Modding.Core/Bindings/ComponentPointerRegistry.cs) · [Policy tests](../../../tests/Karpik.Engine.Core.Runner.Tests/PointerPolicyTests.cs) · [Lua token tests](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaPointerValuesTests.cs)
