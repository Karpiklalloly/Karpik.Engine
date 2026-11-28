namespace Karpik.Engine.Shared.Modding.Bindings;

public enum SchemaValueKind { Scalar, Struct, Fixed, Enum, Pointer }

/// <summary>Immutable schema metadata; arrays are defensively copied when a ModSet is frozen.</summary>
public sealed record SchemaValue
{
    private SchemaValue() { }
    public SchemaValueKind NodeKind { get; init; }
    public SchemaScalarKind ScalarKind { get; init; }
    public SchemaField[] Fields { get; init; } = [];
    public SchemaValue? Element { get; init; }
    public int Length { get; init; }
    public string? PointerTypeId { get; init; }
    public static SchemaValue Scalar(SchemaScalarKind kind) => new() { NodeKind = SchemaValueKind.Scalar, ScalarKind = kind };
    public static SchemaValue Struct(params SchemaField[] fields) => new() { NodeKind = SchemaValueKind.Struct, Fields = fields };
    public static SchemaValue Fixed(SchemaValue element, int length) => new() { NodeKind = SchemaValueKind.Fixed, Element = element, Length = length };
    public static SchemaValue Enum(SchemaScalarKind underlying) => new() { NodeKind = SchemaValueKind.Enum, ScalarKind = underlying };
    public static SchemaValue Pointer(string typeId) => new() { NodeKind = SchemaValueKind.Pointer, PointerTypeId = typeId };
}

/// <summary>A field name or a one-based fixed-array index in a prepared component path.</summary>
public readonly record struct ComponentPathSegment(string? Name, int Index = 0);

internal static class SchemaTree
{
    internal static ComponentSchema Canonicalize(ComponentSchema schema)
    {
        if (string.IsNullOrWhiteSpace(schema.Id) || schema.Version < 1 || schema.Fields is null || schema.Fields.Length == 0)
            throw new InvalidDataException("A schema requires an ID, positive version and fields.");
        SchemaValue root = SchemaValue.Struct(schema.Fields);
        var completed = new Dictionary<SchemaValue, (SchemaValue Value, int Bytes)>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<SchemaValue>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(SchemaValue Node, bool Finish)>();
        pending.Push((root, false));
        try
        {
            while (pending.TryPop(out var frame))
            {
                SchemaValue node = frame.Node;
                if (completed.ContainsKey(node)) continue;
                if (!frame.Finish)
                {
                    if (!active.Add(node)) throw new InvalidDataException($"Cyclic schema '{schema.Id}'.");
                    ValidateNode(node);
                    pending.Push((node, true));
                    if (node.NodeKind == SchemaValueKind.Struct)
                    {
                        foreach (SchemaField field in node.Fields)
                            if (field.Value is not null) pending.Push((field.Value, false));
                    }
                    else if (node.NodeKind == SchemaValueKind.Fixed) pending.Push((node.Element!, false));
                    continue;
                }
                SchemaValue canonical = node;
                int bytes;
                if (node.NodeKind == SchemaValueKind.Struct)
                {
                    SchemaField[] fields = node.Fields.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
                    bytes = 0;
                    for (int i = 0; i < fields.Length; i++)
                    {
                        SchemaField field = fields[i];
                        if (string.IsNullOrWhiteSpace(field.Name) || i > 0 && fields[i - 1].Name == field.Name)
                            throw new InvalidDataException($"Invalid or duplicate fields in schema '{schema.Id}'.");
                        if (field.Value is { } value)
                        {
                            var child = completed[value];
                            fields[i] = child.Value.NodeKind == SchemaValueKind.Scalar
                                ? new SchemaField(field.Name, child.Value.ScalarKind) : new SchemaField(field.Name, child.Value);
                            bytes = checked(bytes + child.Bytes);
                        }
                        else bytes = checked(bytes + ScalarSize(field.Kind));
                    }
                    canonical = SchemaValue.Struct(fields);
                }
                else if (node.NodeKind == SchemaValueKind.Fixed)
                {
                    var child = completed[node.Element!];
                    bytes = checked(child.Bytes * node.Length);
                    canonical = SchemaValue.Fixed(child.Value, node.Length);
                }
                else if (node.NodeKind == SchemaValueKind.Pointer) bytes = System.Runtime.CompilerServices.Unsafe.SizeOf<ComponentPointerValue>();
                else bytes = ScalarSize(node.ScalarKind);
                active.Remove(node);
                completed.Add(node, (canonical, bytes));
            }
        }
        catch (OverflowException error) { throw new InvalidDataException($"Schema '{schema.Id}' layout is too large.", error); }
        return new(schema.Id, schema.Version, completed[root].Value.Fields);
    }

    private static void ValidateNode(SchemaValue node)
    {
        if (node.Fields is null) throw new InvalidDataException("Schema fields cannot be null.");
        switch (node.NodeKind)
        {
            case SchemaValueKind.Scalar:
                ScalarSize(node.ScalarKind);
                if (node.Fields.Length != 0 || node.Element is not null || node.Length != 0 || node.PointerTypeId is not null)
                    throw new InvalidDataException("Scalar schema contains composite options.");
                break;
            case SchemaValueKind.Enum:
                if (node.ScalarKind is not (SchemaScalarKind.I8 or SchemaScalarKind.U8 or SchemaScalarKind.I16 or SchemaScalarKind.U16 or SchemaScalarKind.I32 or SchemaScalarKind.U32 or SchemaScalarKind.I64 or SchemaScalarKind.U64))
                    throw new InvalidDataException("Enum requires a fixed-width integer underlying kind.");
                if (node.Fields.Length != 0 || node.Element is not null || node.Length != 0 || node.PointerTypeId is not null)
                    throw new InvalidDataException("Enum schema contains composite options.");
                break;
            case SchemaValueKind.Struct:
                if (node.Fields is null || node.Fields.Length == 0 || node.Element is not null || node.Length != 0 || node.PointerTypeId is not null)
                    throw new InvalidDataException("Struct requires nonempty fields and no array/pointer options.");
                break;
            case SchemaValueKind.Fixed:
                if (node.Length < 1 || node.Element is null || node.Element.NodeKind == SchemaValueKind.Fixed || node.Fields.Length != 0 || node.PointerTypeId is not null)
                    throw new InvalidDataException("Fixed buffer requires a positive length and scalar or struct element.");
                break;
            case SchemaValueKind.Pointer:
                if (string.IsNullOrWhiteSpace(node.PointerTypeId) || node.Fields.Length != 0 || node.Element is not null || node.Length != 0)
                    throw new InvalidDataException("Pointer schema requires only a host type ID.");
                break;
            default: throw new InvalidDataException("Unknown schema node kind.");
        }
    }

    internal static int ScalarSize(SchemaScalarKind kind) => kind switch
    {
        SchemaScalarKind.Bool or SchemaScalarKind.I8 or SchemaScalarKind.U8 => 1,
        SchemaScalarKind.I16 or SchemaScalarKind.U16 or SchemaScalarKind.Char => 2,
        SchemaScalarKind.I32 or SchemaScalarKind.U32 or SchemaScalarKind.F32 => 4,
        SchemaScalarKind.I64 or SchemaScalarKind.U64 or SchemaScalarKind.F64 => 8,
        SchemaScalarKind.Decimal => 16,
        SchemaScalarKind.NInt or SchemaScalarKind.NUInt => IntPtr.Size,
        _ => throw new InvalidDataException("Unsupported scalar schema kind.")
    };

    internal static bool Same(ComponentSchema a, ComponentSchema b) =>
        a.Id == b.Id && a.Version == b.Version && SameFields(a.Fields, b.Fields);

    internal static bool SameFields(SchemaField[] a, SchemaField[] b)
    {
        var pending = new Stack<(SchemaField[] A, SchemaField[] B)>();
        pending.Push((a, b));
        var values = new Stack<(SchemaValue A, SchemaValue B)>();
        while (pending.Count != 0 || values.Count != 0)
        {
            if (pending.TryPop(out var pair))
            {
                if (pair.A.Length != pair.B.Length) return false;
                for (int i = 0; i < pair.A.Length; i++)
                {
                    var x = pair.A[i]; var y = pair.B[i];
                    if (x.Name != y.Name || x.Kind != y.Kind || (x.Value is null) != (y.Value is null)) return false;
                    if (x.Value is not null) values.Push((x.Value, y.Value!));
                }
            }
            else
            {
                var (x, y) = values.Pop();
                if (x.NodeKind != y.NodeKind || x.ScalarKind != y.ScalarKind || x.Length != y.Length || x.PointerTypeId != y.PointerTypeId) return false;
                if (x.NodeKind == SchemaValueKind.Struct) pending.Push((x.Fields, y.Fields));
                if (x.NodeKind == SchemaValueKind.Fixed) values.Push((x.Element!, y.Element!));
            }
        }
        return true;
    }
}
