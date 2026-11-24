using Karpik.Engine.Shared.Modding.Bindings;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Karpik.Engine.Shared.Modding.Lua;

/// <summary>Explicit, immutable Lua pointer tokens. No automatic CLR interop or raw-address member is exposed.</summary>
internal static class LuaPointerValues
{
    private static readonly PointerDescriptor Descriptor = new();

    internal static DynValue Create(ComponentPointerValue value, ComponentPointerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Validate(value, registry);
        return value.IsNull ? DynValue.Nil : UserData.Create(new PointerValue(value, registry), Descriptor);
    }

    internal static ComponentPointerValue Read(DynValue value, ComponentPointerRegistry registry, ComponentPointerType expected)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(registry);
        ComponentPointerValue pointer;
        if (value.IsNil()) pointer = default;
        else if (value.Type == DataType.UserData && value.UserData.Object is PointerValue wrapper && ReferenceEquals(wrapper.Registry, registry))
            pointer = wrapper.Value;
        else throw new ScriptRuntimeException("An opaque host pointer token or nil is required.");
        try { registry.ValidateAssignment(pointer, expected); }
        catch (InvalidOperationException error) { throw PolicyError(error); }
        return pointer;
    }

    private static void Validate(ComponentPointerValue value, ComponentPointerRegistry registry)
    {
        try { registry.Validate(value); }
        catch (InvalidOperationException error) { throw PolicyError(error); }
    }

    private static ScriptRuntimeException PolicyError(Exception error) => new($"Opaque pointer access rejected: {error.Message}");

    private sealed class PointerValue(ComponentPointerValue value, ComponentPointerRegistry registry)
    {
        internal ComponentPointerValue Value { get; } = value;
        internal ComponentPointerRegistry Registry { get; } = registry;
        internal void RequireValid() => Validate(Value, Registry);
    }

    private sealed class PointerDescriptor : IUserDataDescriptor
    {
        public string Name => "opaque pointer";
        public Type Type => typeof(PointerValue);

        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            ((PointerValue)obj).RequireValid();
            throw new ScriptRuntimeException("Opaque pointer tokens expose no members.");
        }

        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        {
            ((PointerValue)obj).RequireValid();
            throw new ScriptRuntimeException("Opaque pointer tokens are immutable and expose no members.");
        }

        public string AsString(object obj)
        {
            ((PointerValue)obj).RequireValid();
            return "opaque pointer";
        }

        public DynValue MetaIndex(Script script, object obj, string metaname)
        {
            ((PointerValue)obj).RequireValid();
            return null!;
        }

        public bool IsTypeCompatible(Type type, object obj) => type == typeof(PointerValue) && obj is PointerValue;
    }
}
