using Xunit;

namespace Karpik.Engine.Core.Runner.Tests;

/// <summary>
/// NativeAOT cannot compile generic method instantiations created through
/// MethodInfo.MakeGenericMethod at runtime. The ECS component array converter
/// must deserialize through non-generic Newtonsoft overloads so the hot-reload
/// state pipeline keeps working inside published static hosts.
/// </summary>
public sealed class EcsStateSerializationSourceBoundaryTests
{
    private const string ConverterSourceFile = "ComponentArrayConverter.cs";

    [Fact]
    public void ComponentArrayConverter_DoesNotUseRuntimeGenericMethodInstantiation()
    {
        string sourceRoot = Path.Combine(AppContext.BaseDirectory, "EcsCoreSources");
        Assert.True(Directory.Exists(sourceRoot), $"Missing ECS.Core source copies: {sourceRoot}");

        string converterPath = Directory
            .EnumerateFiles(sourceRoot, ConverterSourceFile, SearchOption.AllDirectories)
            .SingleOrDefault()
            ?? throw new InvalidOperationException($"Converter source not found under {sourceRoot}");

        string source = File.ReadAllText(converterPath);
        Assert.True(!source.Contains("MakeGenericMethod", StringComparison.Ordinal),
            "ComponentArrayConverter must not instantiate generic methods at runtime (NativeAOT incompatible).");
        Assert.True(!source.Contains("IsGenericMethodDefinition", StringComparison.Ordinal),
            "ComponentArrayConverter must not look up generic method definitions at runtime (NativeAOT incompatible).");
        Assert.Contains("ToObject(componentType, serializer)", source, StringComparison.Ordinal);
    }
}
