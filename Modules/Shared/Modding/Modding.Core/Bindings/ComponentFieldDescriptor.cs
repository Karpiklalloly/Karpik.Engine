namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed record ComponentFieldDescriptor
{
    // This reference holds prepared accessor metadata, never a boxed component value.
    private readonly object _field;
    public ReadOnlyMemory<ComponentPathSegment> Path { get; init; }
    public ComponentPointerRegistry? PointerRegistry { get; init; }
    public ComponentPointerType PointerType { get; init; }
    public IComponentField<ComponentPointerValue> Pointer => Get<ComponentPointerValue>();
    public string Name { get; init; }
    public bool CanWrite { get; init; }
    public SchemaScalarKind Kind { get; }
    public IComponentField<bool> Bool => Get<bool>();
    public IComponentField<int> I32 => Get<int>();
    public IComponentField<uint> U32 => Get<uint>();
    public IComponentField<float> F32
    {
        get => Get<float>();
        init
        {
            if (Kind != SchemaScalarKind.F32) throw WrongKind();
            _field = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    public IComponentField<double> F64 => Get<double>();
    public IComponentField<sbyte> I8 => Get<sbyte>();
    public IComponentField<byte> U8 => Get<byte>();
    public IComponentField<short> I16 => Get<short>();
    public IComponentField<ushort> U16 => Get<ushort>();
    public IComponentField<char> Char => Get<char>();
    public IComponentField<long> I64 => Get<long>();
    public IComponentField<ulong> U64 => Get<ulong>();
    public IComponentField<decimal> Decimal => Get<decimal>();
    public IComponentField<nint> NInt => Get<nint>();
    public IComponentField<nuint> NUInt => Get<nuint>();

    internal ComponentFieldDescriptor(string name, bool canWrite, SchemaScalarKind kind, object field)
    { Name = name; CanWrite = canWrite; Kind = kind; _field = field; }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<bool> field)
        : this(Name, CanWrite, SchemaScalarKind.Bool, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<int> field)
        : this(Name, CanWrite, SchemaScalarKind.I32, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<uint> field)
        : this(Name, CanWrite, SchemaScalarKind.U32, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<float> F32)
        : this(Name, CanWrite, SchemaScalarKind.F32, F32 ?? throw new ArgumentNullException(nameof(F32))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<double> field)
        : this(Name, CanWrite, SchemaScalarKind.F64, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<sbyte> field)
        : this(Name, CanWrite, SchemaScalarKind.I8, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<byte> field)
        : this(Name, CanWrite, SchemaScalarKind.U8, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<short> field)
        : this(Name, CanWrite, SchemaScalarKind.I16, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<ushort> field)
        : this(Name, CanWrite, SchemaScalarKind.U16, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<char> field)
        : this(Name, CanWrite, SchemaScalarKind.Char, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<long> field)
        : this(Name, CanWrite, SchemaScalarKind.I64, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<ulong> field)
        : this(Name, CanWrite, SchemaScalarKind.U64, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<decimal> field)
        : this(Name, CanWrite, SchemaScalarKind.Decimal, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<nint> field)
        : this(Name, CanWrite, SchemaScalarKind.NInt, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<nuint> field)
        : this(Name, CanWrite, SchemaScalarKind.NUInt, field ?? throw new ArgumentNullException(nameof(field))) { }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<ComponentPointerValue> field)
        : this(Name, CanWrite, SchemaScalarKind.Pointer, field ?? throw new ArgumentNullException(nameof(field))) { }
    public IComponentField<TValue> Get<TValue>() where TValue : unmanaged
    {
        if (Kind != KindOf<TValue>() || _field is not IComponentField<TValue> accessor) throw WrongKind();
        return accessor;
    }
    internal ComponentFieldDescriptor BindTo(PreparedComponentHandle handle)
    {
        return new(Name, CanWrite, Kind, Kind switch
        {
            SchemaScalarKind.Bool => Guard<bool>(), SchemaScalarKind.I8 => Guard<sbyte>(),
            SchemaScalarKind.U8 => Guard<byte>(), SchemaScalarKind.I16 => Guard<short>(),
            SchemaScalarKind.U16 => Guard<ushort>(), SchemaScalarKind.Char => Guard<char>(),
            SchemaScalarKind.I32 => Guard<int>(), SchemaScalarKind.U32 => Guard<uint>(),
            SchemaScalarKind.I64 => Guard<long>(), SchemaScalarKind.U64 => Guard<ulong>(),
            SchemaScalarKind.F32 => Guard<float>(), SchemaScalarKind.F64 => Guard<double>(),
            SchemaScalarKind.Decimal => Guard<decimal>(), SchemaScalarKind.NInt => Guard<nint>(),
            SchemaScalarKind.NUInt => Guard<nuint>(), SchemaScalarKind.Pointer => Guard<ComponentPointerValue>(),
            _ => throw WrongKind()
        }) { Path = Path, PointerRegistry = PointerRegistry, PointerType = PointerType };

        ScopedField<T> Guard<T>() where T : unmanaged => new(handle, Get<T>(), CanWrite);
    }

    private sealed class ScopedField<T>(PreparedComponentHandle handle, IComponentField<T> field, bool canWrite) :
        IComponentField<T> where T : unmanaged
    {
        public T Read(int entityId) { handle.Require(); return field.Read(entityId); }
        public void Write(int entityId, T value)
        {
            handle.RequireMutable();
            if (!canWrite) throw new InvalidOperationException("Component field is readonly.");
            field.Write(entityId, value);
        }
    }

    // The JIT specializes this immutable type-to-kind mapping; values are never boxed.
    private static SchemaScalarKind KindOf<TValue>() where TValue : unmanaged =>
        typeof(TValue) == typeof(bool) ? SchemaScalarKind.Bool :
        typeof(TValue) == typeof(int) ? SchemaScalarKind.I32 :
        typeof(TValue) == typeof(uint) ? SchemaScalarKind.U32 :
        typeof(TValue) == typeof(float) ? SchemaScalarKind.F32 :
        typeof(TValue) == typeof(double) ? SchemaScalarKind.F64 :
        typeof(TValue) == typeof(sbyte) ? SchemaScalarKind.I8 :
        typeof(TValue) == typeof(byte) ? SchemaScalarKind.U8 :
        typeof(TValue) == typeof(short) ? SchemaScalarKind.I16 :
        typeof(TValue) == typeof(ushort) ? SchemaScalarKind.U16 :
        typeof(TValue) == typeof(char) ? SchemaScalarKind.Char :
        typeof(TValue) == typeof(long) ? SchemaScalarKind.I64 :
        typeof(TValue) == typeof(ulong) ? SchemaScalarKind.U64 :
        typeof(TValue) == typeof(decimal) ? SchemaScalarKind.Decimal :
        typeof(TValue) == typeof(nint) ? SchemaScalarKind.NInt :
        typeof(TValue) == typeof(nuint) ? SchemaScalarKind.NUInt :
        typeof(TValue) == typeof(ComponentPointerValue) ? SchemaScalarKind.Pointer :
        (SchemaScalarKind)(-1);
    public void Deconstruct(out string Name, out bool CanWrite, out IComponentField<float> F32)
    { Name = this.Name; CanWrite = this.CanWrite; F32 = this.F32; }
    public override string ToString() => $"{nameof(ComponentFieldDescriptor)} {{ Name = {Name}, CanWrite = {CanWrite}, Kind = {Kind} }}";
    private InvalidOperationException WrongKind() => new($"Field '{Name}' has kind {Kind}.");
}
