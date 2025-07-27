using System;

namespace Karpik.StatAndAbilities
{
    [Serializable]
    public readonly struct Buff : IEquatable<Buff>
    {
        public readonly float Value;
        public readonly BuffType Type;
        public readonly bool ModifyBase;

        public Buff(float value, BuffType type, bool modifyBase = false)
        {
            Value = value;
            Type = type;
            ModifyBase = modifyBase;
        }

        public bool Equals(Buff other)
        {
            return Value.Equals(other.Value) && Type == other.Type && ModifyBase == other.ModifyBase;
        }

        public override bool Equals(object obj)
        {
            return obj is Buff other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value, (int)Type, ModifyBase);
        }

        public static bool operator ==(Buff left, Buff right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Buff left, Buff right)
        {
            return !(left == right);
        }
    }
}