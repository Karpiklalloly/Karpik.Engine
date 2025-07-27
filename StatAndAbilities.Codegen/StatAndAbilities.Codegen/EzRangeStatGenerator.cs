using System.Collections.Generic;

namespace Karpik.StatAndAbilities.Codegen
{
    public static class EzRangeStatGenerator
    {
        public static List<(string, string)> Generate(string structName, string namespaceName, string accessibility = "public")
        {
            return new List<(string, string)>
            {
                GenerateEzRangeStat(structName, namespaceName, accessibility),
                GenerateEzRangeStatExtensions(structName, namespaceName)
            };
        }
        
        private static (string, string) GenerateEzRangeStat(string name, string namespaceName, string accessibility = "public")
        {
            var source = 
                $@"using Karpik.StatAndAbilities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
                
namespace {namespaceName}
{{
    [Serializable][AllowedInWorlds(typeof(EcsDefaultWorld), nameof(EcsDefaultWorld))]
    {accessibility} partial struct {name} : IEzRangeStat
    {{
        public float Value
        {{
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _value;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {{
                if (value < Min.ModifiedValue)
                {{
                    value = Min.ModifiedValue;
                }}
                else if (value > Max.ModifiedValue)
                {{
                    value = Max.ModifiedValue;
                }}
                _value = value;
            }}
        }}

        private float _value;
        public DefaultStat Min;
        public DefaultStat Max;
        
        public void Init()
        {{
            Min.Init();
            Max.Init();
        }}
        
        public void DeInit()
        {{
            Min.DeInit();
            Max.DeInit();
        }}
    }}
}}";
            
            return ($"{name}.EzRangeStat.g.cs", source);
        }
        
        private static (string, string) GenerateEzRangeStatExtensions(string name, string namespaceName)
        {
            var source = 
                $@"using Karpik.StatAndAbilities;
using System;
using System.Runtime.CompilerServices;

namespace {namespaceName}
{{
    public static partial class {name}Extensions
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyEffect(ref this {name} stat, Effect effect, BuffEzRange buff)
        {{
            if (buff.Flagged(BuffEzRange.Min)) stat.Min.ApplyEffect(effect);
            if (buff.Flagged(BuffEzRange.Max)) stat.Max.ApplyEffect(effect);
            stat.Value = stat.Value;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyBuffInstantly(ref this {name} stat, Buff buff, BuffEzRange buffRange)
        {{
            if (buffRange.Flagged(BuffEzRange.Min)) stat.Min.ApplyBuffInstantly(buff);
            if (buffRange.Flagged(BuffEzRange.Max)) stat.Max.ApplyBuffInstantly(buff);
            stat.Value = stat.Value;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveEffect(ref this {name} stat, Effect effect, BuffEzRange buff = BuffEzRange.All)
        {{
            if (buff.Flagged(BuffEzRange.Min)) stat.Min.RemoveEffect(effect);
            if (buff.Flagged(BuffEzRange.Max)) stat.Max.RemoveEffect(effect);
            stat.Value = stat.Value;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveEffect(ref this {name} stat, string name, BuffEzRange buff = BuffEzRange.All)
        {{
            if (buff.Flagged(BuffEzRange.Min)) stat.Min.RemoveEffect(name);
            if (buff.Flagged(BuffEzRange.Max)) stat.Max.RemoveEffect(name);
            stat.Value = stat.Value;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearEffects(ref this {name} stat)
        {{
            stat.Min.ClearEffects();
            stat.Max.ClearEffects();
            stat.Value = stat.Value;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<Buff> BuffsMin(ref this {name} stat) => stat.Min.Buffs();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<Buff> BuffsMax(ref this {name} stat) => stat.Max.Buffs();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ActualizeEffects(ref this {name} stat)
        {{
            stat.Min.ActualizeEffects();
            stat.Max.ActualizeEffects();
            stat.Value = stat.Value;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinModified(ref this {name} stat) => stat.Min.ModifiedValue;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxModified(ref this {name} stat) => stat.Max.ModifiedValue;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IsOnTheEdge(ref this {name} stat)
        {{
            if (stat.Value == stat.Min.ModifiedValue) return -1;
            if (stat.Value == stat.Max.ModifiedValue) return 1;
            return 0;
        }}
    }}
}}";
            return ($"{name}.EzRangeStat.Extensions.g.cs", source);
        }
    }
}