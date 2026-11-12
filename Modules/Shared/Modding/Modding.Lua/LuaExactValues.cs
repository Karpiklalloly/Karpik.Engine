using System.Globalization;
using System.Numerics;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Karpik.Engine.Shared.Modding.Lua;

/// <summary>
/// Exact Lua values have immutable value semantics. Their descriptors are stateless metadata;
/// individual Lua values are deliberately allocated rather than retargeting a retained value.
/// </summary>
internal static class LuaExactValues
{
    private static readonly ExactDescriptor<long> I64 = new("i64");
    private static readonly ExactDescriptor<ulong> U64 = new("u64");
    private static readonly ExactDescriptor<decimal> Decimal = new("decimal");
    private static readonly DynValue I64Constructor = DynValue.NewCallback((_, args) => Construct(args, "i64"));
    private static readonly DynValue U64Constructor = DynValue.NewCallback((_, args) => Construct(args, "u64"));
    private static readonly DynValue DecimalConstructor = DynValue.NewCallback((_, args) => Construct(args, "decimal"));

    internal static void Register(Script script)
    {
        var types = new Table(script);
        types.Set("i64", I64Constructor);
        types.Set("u64", U64Constructor);
        types.Set("decimal", DecimalConstructor);
        script.Globals.Set("types", DynValue.NewTable(types));
    }

    internal static DynValue Create(long value) => I64.Create(value);
    internal static DynValue Create(ulong value) => U64.Create(value);
    internal static DynValue Create(decimal value) => Decimal.Create(value);
    internal static long ReadI64(DynValue value) => I64.Read(value);
    internal static ulong ReadU64(DynValue value) => U64.Read(value);
    internal static decimal ReadDecimal(DynValue value) => Decimal.Read(value);

    private static DynValue Construct(CallbackArguments args, string kind)
    {
        if (args.Count != 1) throw new ScriptRuntimeException($"types.{kind} requires one value.");
        try
        {
            return kind switch
            {
                "i64" => Create(ConvertI64(args[0])),
                "u64" => Create(ConvertU64(args[0])),
                _ => Create(ConvertDecimal(args[0]))
            };
        }
        catch (Exception error) when (error is OverflowException or FormatException)
        {
            throw new ScriptRuntimeException($"types.{kind}: {error.Message}");
        }
    }

    private static long ConvertI64(DynValue value)
    {
        if (value.Type == DataType.String)
        {
            ValidateDigits(value.String, allowPoint: false);
            return long.Parse(value.String, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        if (value.Type == DataType.Number)
        {
            double number = value.Number;
            if (!double.IsFinite(number) || number != Math.Truncate(number) ||
                number < -9223372036854775808d || number >= 9223372036854775808d)
                throw new OverflowException("A finite integer within the i64 range is required.");
            return checked((long)number);
        }
        if (value.Type == DataType.UserData)
        {
            return value.UserData.Object switch
            {
                ExactValue<long> number => number.Value,
                ExactValue<ulong> number => checked((long)number.Value),
                ExactValue<decimal> number when decimal.Truncate(number.Value) == number.Value => checked((long)number.Value),
                ExactValue<decimal> => throw new FormatException("Conversion to i64 would discard a fractional part."),
                _ => throw new FormatException("An exact numeric value is required.")
            };
        }
        throw new FormatException("A numeric string, number or exact numeric value is required.");
    }

    private static ulong ConvertU64(DynValue value)
    {
        if (value.Type == DataType.String)
        {
            ValidateDigits(value.String, allowPoint: false);
            return ulong.Parse(value.String, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        if (value.Type == DataType.Number)
        {
            double number = value.Number;
            if (!double.IsFinite(number) || number != Math.Truncate(number) || number < 0 || number >= 18446744073709551616d)
                throw new OverflowException("A finite integer within the u64 range is required.");
            return checked((ulong)number);
        }
        if (value.Type == DataType.UserData)
        {
            return value.UserData.Object switch
            {
                ExactValue<ulong> number => number.Value,
                ExactValue<long> number => checked((ulong)number.Value),
                ExactValue<decimal> number when decimal.Truncate(number.Value) == number.Value => checked((ulong)number.Value),
                ExactValue<decimal> => throw new FormatException("Conversion to u64 would discard a fractional part."),
                _ => throw new FormatException("An exact numeric value is required.")
            };
        }
        throw new FormatException("A numeric string, number or exact numeric value is required.");
    }

    private static decimal ConvertDecimal(DynValue value)
    {
        if (value.Type == DataType.String) return ParseDecimal(value.String);
        if (value.Type == DataType.Number) return DecimalFromDouble(value.Number);
        if (value.Type == DataType.UserData)
        {
            return value.UserData.Object switch
            {
                ExactValue<decimal> number => number.Value,
                ExactValue<long> number => number.Value,
                ExactValue<ulong> number => number.Value,
                _ => throw new FormatException("An exact numeric value is required.")
            };
        }
        throw new FormatException("A numeric string, number or exact numeric value is required.");
    }

    // Explicit parsing/conversion paths may allocate BigInteger temporaries. Arithmetic and
    // typed ECS access do not use BigInteger. Never use decimal.Parse's rounding here.
    private static decimal ParseDecimal(string text)
    {
        ValidateDigits(text, allowPoint: true);
        bool negative = text[0] == '-';
        int first = text[0] is '-' or '+' ? 1 : 0;
        BigInteger coefficient = BigInteger.Zero;
        int scale = 0;
        bool fractional = false;
        for (int i = first; i < text.Length; i++)
        {
            if (text[i] == '.') { fractional = true; continue; }
            coefficient = coefficient * 10 + (text[i] - '0');
            if (fractional) scale++;
        }
        if (coefficient.IsZero) return new decimal(0, 0, 0, negative, (byte)Math.Min(scale, 28));
        BigInteger maximum = (BigInteger.One << 96) - 1;
        while ((scale > 28 || coefficient > maximum) && scale > 0)
        {
            BigInteger quotient = BigInteger.DivRem(coefficient, 10, out BigInteger remainder);
            if (!remainder.IsZero) break;
            coefficient = quotient;
            scale--;
        }
        if (scale > 28 || coefficient > maximum)
            throw new OverflowException("The string cannot be represented exactly as decimal.");
        return MakeDecimal(coefficient, negative, scale);
    }

    private static void ValidateDigits(string text, bool allowPoint)
    {
        if (text.Length == 0) throw new FormatException("An invariant numeric string is required.");
        int first = text[0] is '-' or '+' ? 1 : 0;
        bool point = false, digit = false;
        for (int i = first; i < text.Length; i++)
        {
            char character = text[i];
            if (character is >= '0' and <= '9') { digit = true; continue; }
            if (allowPoint && character == '.' && !point) { point = true; continue; }
            throw new FormatException("Use invariant fixed-point digits without whitespace or exponent notation.");
        }
        if (!digit) throw new FormatException("At least one numeric digit is required.");
    }

    private static decimal DecimalFromDouble(double number)
    {
        if (!double.IsFinite(number)) throw new OverflowException("A finite number is required.");
        ulong bits = BitConverter.DoubleToUInt64Bits(number);
        bool negative = (bits >> 63) != 0;
        int encodedExponent = (int)((bits >> 52) & 0x7ff);
        ulong significand = bits & ((1UL << 52) - 1);
        int exponent = encodedExponent == 0 ? -1074 : encodedExponent - 1023 - 52;
        if (encodedExponent != 0) significand |= 1UL << 52;
        if (significand == 0) return new decimal(0, 0, 0, negative, 0);
        while (exponent < 0 && (significand & 1) == 0) { significand >>= 1; exponent++; }
        int scale = exponent < 0 ? -exponent : 0;
        if (scale > 28) throw new OverflowException("The Lua number cannot be represented exactly as decimal.");
        BigInteger coefficient = exponent >= 0
            ? new BigInteger(significand) << exponent
            : new BigInteger(significand) * BigInteger.Pow(5, scale);
        if (coefficient >= (BigInteger.One << 96))
            throw new OverflowException("The Lua number is outside the decimal range.");
        return MakeDecimal(coefficient, negative, scale);
    }

    private static decimal MakeDecimal(BigInteger coefficient, bool negative, int scale) => new(
        unchecked((int)(uint)(coefficient & uint.MaxValue)),
        unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
        unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)), negative, (byte)scale);

    private sealed class ExactValue<T>(T value) where T : struct
    {
        internal T Value { get; } = value;
    }

    private enum Operation { Add, Subtract, Multiply, Divide, Remainder, Negate, Equal, Less, LessOrEqual }

    private sealed class ExactDescriptor<T> : IUserDataDescriptor where T : struct, INumber<T>
    {
        private readonly DynValue _add, _subtract, _multiply, _divide, _remainder, _negate, _equal, _less, _lessOrEqual;
        public ExactDescriptor(string name)
        {
            Name = name;
            _add = Callback(Operation.Add); _subtract = Callback(Operation.Subtract);
            _multiply = Callback(Operation.Multiply); _divide = Callback(Operation.Divide);
            _remainder = Callback(Operation.Remainder); _negate = Callback(Operation.Negate);
            _equal = Callback(Operation.Equal); _less = Callback(Operation.Less); _lessOrEqual = Callback(Operation.LessOrEqual);
        }
        public string Name { get; }
        public Type Type => typeof(ExactValue<T>);
        internal DynValue Create(T value) => UserData.Create(new ExactValue<T>(value), this);
        internal T Read(DynValue value) => value.Type == DataType.UserData && value.UserData.Object is ExactValue<T> exact
            ? exact.Value : throw new ScriptRuntimeException($"Expected an exact {Name} value; use types.{Name} explicitly.");
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing) =>
            throw new ScriptRuntimeException($"Exact {Name} values have no members.");
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) =>
            throw new ScriptRuntimeException($"Exact {Name} values are immutable.");
        public string AsString(object obj) => ((ExactValue<T>)obj).Value.ToString(null, CultureInfo.InvariantCulture);
        public bool IsTypeCompatible(Type type, object obj) => type == Type;
        public DynValue MetaIndex(Script script, object obj, string metaname) => metaname switch
        {
            "__add" => _add, "__sub" => _subtract, "__mul" => _multiply, "__div" => _divide,
            "__mod" => _remainder, "__unm" => _negate, "__eq" => _equal,
            "__lt" => _less, "__le" => _lessOrEqual, _ => null!
        };
        private DynValue Callback(Operation operation) => DynValue.NewCallback((_, args) => Apply(operation, args));
        private DynValue Apply(Operation operation, CallbackArguments args)
        {
            if (operation == Operation.Equal)
                return DynValue.NewBoolean(args[0].Type == DataType.UserData && args[1].Type == DataType.UserData &&
                    args[0].UserData.Object is ExactValue<T> first && args[1].UserData.Object is ExactValue<T> second && first.Value == second.Value);
            T left = Read(args[0]);
            try
            {
                if (operation == Operation.Negate) return Create(checked(-left));
                T right = Read(args[1]);
                return operation switch
                {
                    Operation.Add => Create(checked(left + right)),
                    Operation.Subtract => Create(checked(left - right)),
                    Operation.Multiply => Create(checked(left * right)),
                    Operation.Divide => Create(checked(left / right)),
                    Operation.Remainder => Create(left % right),
                    Operation.Less => DynValue.NewBoolean(left < right),
                    Operation.LessOrEqual => DynValue.NewBoolean(left <= right),
                    _ => throw new ScriptRuntimeException("Unsupported exact numeric operator.")
                };
            }
            catch (Exception error) when (error is OverflowException or DivideByZeroException)
            {
                throw new ScriptRuntimeException($"Exact {Name} {operation}: {error.Message}");
            }
        }
    }
}
