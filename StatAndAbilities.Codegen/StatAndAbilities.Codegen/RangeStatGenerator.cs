using System.Collections.Generic;

namespace Karpik.StatAndAbilities.Codegen
{
    public static class RangeStatGenerator
    {
        public static List<(string, string)> Generate(string structName, string namespaceName, string accessibility = "public")
        {
            return new List<(string, string)>
            {
                GenerateRangeStat(structName, namespaceName, accessibility),
                GenerateRangeStatExtensions(structName, namespaceName)
            };
        }
    
        private static (string, string) GenerateRangeStat(string name, string namespaceName, string accessibility = "public")
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
    {accessibility} partial struct {name} : IRangeStat
    {{
        [IgnoreDataMember]
        public float BaseValue
        {{
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value.BaseValue;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Value.BaseValue = value;
        }}

        [IgnoreDataMember]
        public float ModifiedValue
        {{
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value.ModifiedValue;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Value.ModifiedValue = value;
        }}

        public DefaultStat Value;
        public DefaultStat Min;
        public DefaultStat Max;
        
        public void Init()
        {{
            Value.Init();
            Min.Init();
            Max.Init();
        }}
        
        public void DeInit()
        {{
            Value.DeInit();
            Min.DeInit();
            Max.DeInit();
        }}
    }}
}}";
            
            return ($"{name}.RangeStat.g.cs", source);
        }
    
        private static (string, string) GenerateRangeStatExtensions(string name, string namespaceName)
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
        public static void ApplyEffect(ref this {name} stat, Effect effect, BuffRange buff)
        {{
            if (buff.Flagged(BuffRange.Min)) stat.Min.ApplyEffect(effect);
            if (buff.Flagged(BuffRange.Max)) stat.Max.ApplyEffect(effect);
            if (buff.Flagged(BuffRange.Value)) stat.Value.ApplyEffect(effect);

            stat.ToBounds();
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyBuffInstantly(ref this {name} stat, Buff buff, BuffRange buffRange)
        {{
            if (buffRange.Flagged(BuffRange.Min)) stat.Min.ApplyBuffInstantly(buff);
            if (buffRange.Flagged(BuffRange.Max)) stat.Max.ApplyBuffInstantly(buff);
            if (buffRange.Flagged(BuffRange.Value)) stat.Value.ApplyBuffInstantly(buff);

            stat.ToBounds();
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveEffect(ref this {name} stat, Effect effect, BuffRange buff = BuffRange.All)
        {{
            if (buff.Flagged(BuffRange.Min)) stat.Min.RemoveEffect(effect);
            if (buff.Flagged(BuffRange.Max)) stat.Max.RemoveEffect(effect);
            if (buff.Flagged(BuffRange.Value)) stat.Value.RemoveEffect(effect);

            stat.ToBounds();
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveEffect(ref this {name} stat, string name, BuffRange buff = BuffRange.All)
        {{
            if (buff.Flagged(BuffRange.Min)) stat.Min.RemoveEffect(name);
            if (buff.Flagged(BuffRange.Max)) stat.Max.RemoveEffect(name);
            if (buff.Flagged(BuffRange.Value)) stat.Value.RemoveEffect(name);

            stat.ToBounds();
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearEffects(ref this {name} stat)
        {{
            stat.Min.ClearEffects();
            stat.Max.ClearEffects();
            stat.Value.ClearEffects();

            stat.ToBounds();
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<Buff> BuffsMin(ref this {name} stat) => stat.Min.Buffs();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<Buff> BuffsMax(ref this {name} stat) => stat.Max.Buffs();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<Buff> BuffsValue(ref this {name} stat) => stat.Value.Buffs();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ActualizeEffects(ref this {name} stat)
        {{
            stat.Min.ActualizeEffects();
            stat.Max.ActualizeEffects();
            stat.Value.ActualizeEffects();

            stat.ToBounds();
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ValueModified(ref this {name} stat) => stat.Value.ModifiedValue;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinModified(ref this {name} stat) => stat.Min.ModifiedValue;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxModified(ref this {name} stat) => stat.Max.ModifiedValue;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToBounds(ref this {name} stat)
        {{
            if (stat.Value.ModifiedValue < stat.Min.ModifiedValue) stat.Value.ModifiedValue = stat.Min.ModifiedValue;
            if (stat.Value.ModifiedValue > stat.Max.ModifiedValue) stat.Value.ModifiedValue = stat.Max.ModifiedValue;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOutOfBounds(ref this {name} stat)
        {{
            if (stat.Value.ModifiedValue < stat.Min.ModifiedValue) return true;
            if (stat.Value.ModifiedValue > stat.Max.ModifiedValue) return true;
            return false;
        }}
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IsOnTheEdge(ref this {name} stat)
        {{
            if (stat.Value.ModifiedValue == stat.Min.ModifiedValue) return -1;
            if (stat.Value.ModifiedValue == stat.Max.ModifiedValue) return 1;
            return 0;
        }}
    }}
}}";
            return ($"{name}.RangeStat.Extensions.g.cs", source);
        }
    }
}