namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed record ComponentFieldDescriptor
{
    private readonly IComponentField<bool>? _bool;
    private readonly IComponentField<int>? _i32;
    private readonly IComponentField<uint>? _u32;
    private readonly IComponentField<float>? _f32;
    private readonly IComponentField<double>? _f64;
    public string Name { get; init; }
    public bool CanWrite { get; init; }
    public SchemaScalarKind Kind { get; }
    public IComponentField<bool> Bool => _bool ?? throw WrongKind();
    public IComponentField<int> I32 => _i32 ?? throw WrongKind();
    public IComponentField<uint> U32 => _u32 ?? throw WrongKind();
    public IComponentField<float> F32 => _f32 ?? throw WrongKind();
    public IComponentField<double> F64 => _f64 ?? throw WrongKind();

    private ComponentFieldDescriptor(string name, bool canWrite, SchemaScalarKind kind)
    { Name = name; CanWrite = canWrite; Kind = kind; }
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<bool> field)
        : this(Name, CanWrite, SchemaScalarKind.Bool) => _bool = field ?? throw new ArgumentNullException(nameof(field));
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<int> field)
        : this(Name, CanWrite, SchemaScalarKind.I32) => _i32 = field ?? throw new ArgumentNullException(nameof(field));
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<uint> field)
        : this(Name, CanWrite, SchemaScalarKind.U32) => _u32 = field ?? throw new ArgumentNullException(nameof(field));
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<float> F32)
        : this(Name, CanWrite, SchemaScalarKind.F32) => _f32 = F32 ?? throw new ArgumentNullException(nameof(F32));
    public ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<double> field)
        : this(Name, CanWrite, SchemaScalarKind.F64) => _f64 = field ?? throw new ArgumentNullException(nameof(field));
    public void Deconstruct(out string Name, out bool CanWrite, out IComponentField<float> F32)
    { Name = this.Name; CanWrite = this.CanWrite; F32 = this.F32; }
    public override string ToString() => $"{nameof(ComponentFieldDescriptor)} {{ Name = {Name}, CanWrite = {CanWrite}, Kind = {Kind} }}";
    private InvalidOperationException WrongKind() => new($"Field '{Name}' has kind {Kind}.");
}
