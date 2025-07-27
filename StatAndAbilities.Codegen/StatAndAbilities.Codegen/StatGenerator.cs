using System.Collections.Generic;

namespace Karpik.StatAndAbilities.Codegen
{
    public static class StatGenerator
    {
        public static List<(string, string)> Generate(string structName, string namespaceName, string accessibility = "public")
        {
            return new List<(string, string)>()
            {
                GenerateStat(structName, namespaceName, accessibility),
                GenerateStatExtensions(structName, namespaceName)
            };
        }
    
        private static (string, string) GenerateStat(string name, string namespaceName, string accessibility = "public")
        {
            var source = 
$@"using Karpik.StatAndAbilities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DCFApixels.DragonECS;
                
namespace {namespaceName}
{{
    [Serializable][AllowedInWorlds(typeof(EcsDefaultWorld), nameof(EcsDefaultWorld))]
    {accessibility} partial struct {name} : IStat
    {{
        public float BaseValue;
        [IgnoreDataMember]
        public float ModifiedValue
        {{
            get
            {{
                if (IsDirty)
                {{
                    IsDirty = false;
                    this.ActualizeEffects();
                }}
                return _modifiedValue;
            }}
            set
            {{
                _modifiedValue = value;
            }}
        }}
        [IgnoreDataMember]
        public List<Effect> Effects;
        [IgnoreDataMember]
        public bool IsDirty;        

        private float _modifiedValue;

        public void Init()
        {{
            Effects = new List<Effect>();
            IsDirty = true;
        }}
        
        public void DeInit()
        {{
            for (int i = 0; i < Effects.Count; i++)
            {{
                var effect = Effects[i];
                Array.Clear(effect.Buffs, 0, effect.Buffs.Length);
                effect.Buffs = null;
            }}
            Effects.Clear();
            Effects = null;
        }}
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {{
            Init();
        }}
    }}
}}";
            return ($"{name}.Stat.g.cs", source);
        }

        private static (string, string) GenerateStatExtensions(string name, string namespaceName)
        {
            string source = 
$@"using Karpik.StatAndAbilities;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace {namespaceName}
{{
    public static partial class {name}Extensions
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyEffect(ref this {name} stat, Effect effect)
        {{
            for (int i = 0; i < stat.Effects.Count; i++)
            {{
                var other = stat.Effects[i];
                if (effect.Order < other.Order)
                {{
                    stat.Effects.Insert(i, effect);
                    return;
                }}
            }}
            
            stat.Effects.Add(effect);
            stat.IsDirty = true;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyBuffInstantly(ref this {name} stat, Buff buff) => stat.ModifyBase(buff);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasEffect(ref this {name} stat, Effect effect) => stat.Effects.Contains(effect);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveEffect(ref this {name} stat, Effect effect) => stat.Effects.Remove(effect);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveEffect(ref this {name} stat, string name)
        {{
            for (int i = 0; i < stat.Effects.Count; i++)
            {{
                var effect = stat.Effects[i];
                if (effect.Name != name) continue;
                
                stat.RemoveEffect(i);
                return true;
            }}
            return false;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveEffect(ref this {name} stat, int index)
        {{
            if (0 <= index && index < stat.Effects.Count)
            {{
                stat.Effects.RemoveAt(index);
                return true;
            }}
            return false;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearEffects(ref this {name} stat)
        {{
            while (stat.Effects.Count > 0)
            {{
                stat.RemoveEffect(0);
            }}
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<Buff> Buffs(ref this {name} stat) => stat.Effects.SelectMany(e => e.Buffs).ToArray();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ActualizeEffects(ref this {name} stat)
        {{
            var effects = stat.Effects;
            for (int i = effects.Count - 1; i >= 0; i--)
            {{
                var effect = effects[i];
                if (effect.IsPermanent) continue;
                if (effect.Duration > 0) continue;
                
                stat.RemoveEffect(effect);
            }}
            
            var buffs = stat.Buffs();
            foreach (var buff in buffs)
            {{
                if (buff.ModifyBase)
                {{
                    stat.ModifyBase(buff);
                }}
            }}
            
            stat.IsDirty = false;
            stat.ModifiedValue = stat.BaseValue;
            foreach (var buff in buffs)
            {{
                if (!buff.ModifyBase)
                {{
                    stat.ModifyValue(buff);
                }}
            }}
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ModifyBase(ref this {name} stat, Buff buff)
        {{
            switch (buff.Type)
            {{
                case BuffType.Add:
                    stat.BaseValue += buff.Value;
                    break;
                case BuffType.Multiply:
                    stat.BaseValue *= buff.Value;
                    break;
                case BuffType.Set:
                    stat.BaseValue = buff.Value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }}
            stat.IsDirty = true;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ModifyValue(ref this {name} stat, Buff buff)
        {{
            switch (buff.Type)
            {{
                case BuffType.Add:
                    stat.ModifiedValue += buff.Value;
                    break;
                case BuffType.Multiply:
                    stat.ModifiedValue *= buff.Value;
                    break;
                case BuffType.Set:
                    stat.ModifiedValue = buff.Value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }}
        }}
    }}
}}";
            return ($"{name}.Stat.Extensions.g.cs", source);
        }
    }
}