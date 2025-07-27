using System;

namespace Karpik.StatAndAbilities
{
    [Serializable]
    public struct Effect : IEquatable<Effect>
    {
        public string Name;
        public int Order;
        public float Duration;
        public bool IsPermanent;
        public Buff[] Buffs;

        public bool Equals(Effect other)
        {
            return Equals(Buffs, other.Buffs) && Order == other.Order && Duration.Equals(other.Duration) && IsPermanent == other.IsPermanent;
        }

        public override bool Equals(object obj)
        {
            return obj is Effect other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Buffs, Order, Duration, IsPermanent);
        }

        public static bool operator ==(Effect left, Effect right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Effect left, Effect right)
        {
            return !(left == right);
        }
    }
}