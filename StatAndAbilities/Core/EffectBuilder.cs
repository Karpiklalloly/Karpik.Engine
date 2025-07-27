using System;
using System.Linq;

namespace Karpik.StatAndAbilities
{
    public static class EffectBuilder
    {
        public static BuilderPart Start() => new BuilderPart(string.Empty, -1, 0, Array.Empty<Buff>());
    
        public struct BuilderPart
        {
            private string _name;
            private float _duration;
            private int _order;
            private Buff[] _buffs;
        
            public BuilderPart(string name, float duration = -1, int order = 0, Buff[] buffs = null)
            {
                _name = name;
                _duration = duration;
                _order = order;
                _buffs = buffs;
            }
        
            public BuilderPart WithBuffs(params Buff[] buffs)
            {
                _buffs = buffs;
                return this;
            }

            public BuilderPart WithName(string name)
            {
                _name = name;
                return this;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="duration">Set to '-1' to make it permanent</param>
            /// <returns></returns>
            public BuilderPart WithDuration(float duration)
            {
                _duration = duration;
                return this;
            }
        
            public BuilderPart WithOrder(int order)
            {
                _order = order;
                return this;
            }
        
            public Effect Build()
            {
                _buffs ??= Array.Empty<Buff>();
                _name ??= string.Empty;
                if (_duration == 0) _duration = -1;
            
                return BuildUnsafe();
            }

            public Effect BuildUnsafe() =>
                new()
                {
                    Buffs = _buffs.ToArray(),
                    Name = _name,
                    Order = _order,
                    Duration = _duration,
                    IsPermanent = _duration.Equals(-1)
                };
        }
    }
}