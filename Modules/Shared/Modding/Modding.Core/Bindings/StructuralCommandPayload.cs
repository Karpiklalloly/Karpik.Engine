using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Karpik.Engine.Shared.Modding.Bindings;

// Packed typed leaf values; no object values, boxing or native component memcpy.
internal static class StructuralCommandPayload
{
    internal static int Size(SchemaScalarKind kind) => kind switch
    {
        SchemaScalarKind.Bool => Unsafe.SizeOf<bool>(),
        SchemaScalarKind.I8 => Unsafe.SizeOf<sbyte>(),
        SchemaScalarKind.U8 => Unsafe.SizeOf<byte>(),
        SchemaScalarKind.I16 => Unsafe.SizeOf<short>(),
        SchemaScalarKind.U16 => Unsafe.SizeOf<ushort>(),
        SchemaScalarKind.Char => Unsafe.SizeOf<char>(),
        SchemaScalarKind.I32 => Unsafe.SizeOf<int>(),
        SchemaScalarKind.U32 => Unsafe.SizeOf<uint>(),
        SchemaScalarKind.F32 => Unsafe.SizeOf<float>(),
        SchemaScalarKind.F64 => Unsafe.SizeOf<double>(),
        SchemaScalarKind.I64 => Unsafe.SizeOf<long>(),
        SchemaScalarKind.U64 => Unsafe.SizeOf<ulong>(),
        SchemaScalarKind.Decimal => Unsafe.SizeOf<decimal>(),
        SchemaScalarKind.NInt => Unsafe.SizeOf<nint>(),
        SchemaScalarKind.NUInt => Unsafe.SizeOf<nuint>(),
        SchemaScalarKind.Pointer => Unsafe.SizeOf<ComponentPointerValue>(),
        _ => throw new InvalidOperationException("Unsupported command payload field.")
    };

    internal static void Capture(ComponentSnapshot source, int field, SchemaScalarKind kind, Span<byte> target)
    {
        switch (kind)
        {
            case SchemaScalarKind.Bool: Write(target, source.Read<bool>(field)); break;
            case SchemaScalarKind.I8: Write(target, source.Read<sbyte>(field)); break;
            case SchemaScalarKind.U8: Write(target, source.Read<byte>(field)); break;
            case SchemaScalarKind.I16: Write(target, source.Read<short>(field)); break;
            case SchemaScalarKind.U16: Write(target, source.Read<ushort>(field)); break;
            case SchemaScalarKind.Char: Write(target, source.Read<char>(field)); break;
            case SchemaScalarKind.I32: Write(target, source.Read<int>(field)); break;
            case SchemaScalarKind.U32: Write(target, source.Read<uint>(field)); break;
            case SchemaScalarKind.F32: Write(target, source.Read<float>(field)); break;
            case SchemaScalarKind.F64: Write(target, source.Read<double>(field)); break;
            case SchemaScalarKind.I64: Write(target, source.Read<long>(field)); break;
            case SchemaScalarKind.U64: Write(target, source.Read<ulong>(field)); break;
            case SchemaScalarKind.Decimal: Write(target, source.Read<decimal>(field)); break;
            case SchemaScalarKind.NInt: Write(target, source.Read<nint>(field)); break;
            case SchemaScalarKind.NUInt: Write(target, source.Read<nuint>(field)); break;
            case SchemaScalarKind.Pointer: Write(target, source.Read<ComponentPointerValue>(field)); break;
            default: throw new InvalidOperationException("Unsupported command payload field.");
        }
    }

    internal static void Apply(ComponentFieldDescriptor field, int entity, ReadOnlySpan<byte> source)
    {
        switch (field.Kind)
        {
            case SchemaScalarKind.Bool: field.Get<bool>().Write(entity, MemoryMarshal.Read<bool>(source)); break;
            case SchemaScalarKind.I8: field.Get<sbyte>().Write(entity, MemoryMarshal.Read<sbyte>(source)); break;
            case SchemaScalarKind.U8: field.Get<byte>().Write(entity, MemoryMarshal.Read<byte>(source)); break;
            case SchemaScalarKind.I16: field.Get<short>().Write(entity, MemoryMarshal.Read<short>(source)); break;
            case SchemaScalarKind.U16: field.Get<ushort>().Write(entity, MemoryMarshal.Read<ushort>(source)); break;
            case SchemaScalarKind.Char: field.Get<char>().Write(entity, MemoryMarshal.Read<char>(source)); break;
            case SchemaScalarKind.I32: field.Get<int>().Write(entity, MemoryMarshal.Read<int>(source)); break;
            case SchemaScalarKind.U32: field.Get<uint>().Write(entity, MemoryMarshal.Read<uint>(source)); break;
            case SchemaScalarKind.F32: field.Get<float>().Write(entity, MemoryMarshal.Read<float>(source)); break;
            case SchemaScalarKind.F64: field.Get<double>().Write(entity, MemoryMarshal.Read<double>(source)); break;
            case SchemaScalarKind.I64: field.Get<long>().Write(entity, MemoryMarshal.Read<long>(source)); break;
            case SchemaScalarKind.U64: field.Get<ulong>().Write(entity, MemoryMarshal.Read<ulong>(source)); break;
            case SchemaScalarKind.Decimal: field.Get<decimal>().Write(entity, MemoryMarshal.Read<decimal>(source)); break;
            case SchemaScalarKind.NInt: field.Get<nint>().Write(entity, MemoryMarshal.Read<nint>(source)); break;
            case SchemaScalarKind.NUInt: field.Get<nuint>().Write(entity, MemoryMarshal.Read<nuint>(source)); break;
            case SchemaScalarKind.Pointer: field.Get<ComponentPointerValue>().Write(entity, MemoryMarshal.Read<ComponentPointerValue>(source)); break;
            default: throw new InvalidOperationException("Unsupported command payload field.");
        }
    }

    internal static void ValidatePointer(ComponentFieldDescriptor field, ReadOnlySpan<byte> source)
    {
        if (field.Kind == SchemaScalarKind.Pointer)
            (field.PointerRegistry ?? throw new InvalidOperationException("Pointer permission is missing.")).ValidateAssignment(
                MemoryMarshal.Read<ComponentPointerValue>(source), field.PointerType);
    }

    private static void Write<T>(Span<byte> target, T value) where T : unmanaged => MemoryMarshal.Write(target, in value);
}
