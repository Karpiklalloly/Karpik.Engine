using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Network.Codegen;

#pragma warning disable RS2008
[Generator]
public sealed class NetworkGenerator : IIncrementalGenerator
{
    private const string NetworkedComponentAttribute = "Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute";
    private const string NetworkedFieldAttribute = "Karpik.Engine.Shared.Network.Core.NetworkedFieldAttribute";

    private static readonly DiagnosticDescriptor UnsupportedNetworkedField = new(
        "KNET002", "Unsupported networked field type",
        "Networked member '{0}' on component '{1}' has unsupported type '{2}'",
        "Network.Codegen", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateComponentId = new(
        "KNET003", "Duplicate network component ID",
        "Network components '{0}' and '{1}' produce the same deterministic ID 0x{2}",
        "Network.Codegen", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var input = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider);
        context.RegisterSourceOutput(input, static (productionContext, pair) =>
            Execute(productionContext, pair.Left, pair.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (!ProjectTypeDetector.TryGetProjectProperties(optionsProvider, context, out var properties) ||
            !properties.IsRuntime ||
            properties.Side != ProjectTypeDetector.ProjectType.Shared)
        {
            return;
        }

        var components = FindComponents(compilation, context);
        if (components is null)
        {
            return;
        }

        context.AddSource("NetworkSnapshotRegistry.g.cs", SourceText.From(GenerateSource(components), Encoding.UTF8));
    }

    private static IReadOnlyList<ComponentModel>? FindComponents(Compilation compilation, SourceProductionContext context)
    {
        var componentAttribute = compilation.GetTypeByMetadataName(NetworkedComponentAttribute);
        var fieldAttribute = compilation.GetTypeByMetadataName(NetworkedFieldAttribute);
        if (componentAttribute is null || fieldAttribute is null)
        {
            return [];
        }

        var discovered = new List<ComponentModel>();
        ProcessNamespace(compilation.Assembly.GlobalNamespace, componentAttribute, fieldAttribute, discovered, context);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ProcessNamespace(assembly.GlobalNamespace, componentAttribute, fieldAttribute, discovered, context);
        }

        var hasError = false;
        var byId = new Dictionary<ulong, ComponentModel>();
        foreach (var component in discovered)
        {
            hasError |= component.HasUnsupportedMember;
            if (byId.TryGetValue(component.Id, out var existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateComponentId,
                    component.Location,
                    existing.MetadataName,
                    component.MetadataName,
                    component.Id.ToString("X16", CultureInfo.InvariantCulture)));
                hasError = true;
            }
            else
            {
                byId.Add(component.Id, component);
            }
        }

        return hasError ? null : discovered.OrderBy(static component => component.Id).ToArray();
    }

    private static void ProcessNamespace(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol componentAttribute,
        INamedTypeSymbol fieldAttribute,
        List<ComponentModel> discovered,
        SourceProductionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            ProcessType(type, componentAttribute, fieldAttribute, discovered, context);
        }
        foreach (var child in namespaceSymbol.GetNamespaceMembers())
        {
            ProcessNamespace(child, componentAttribute, fieldAttribute, discovered, context);
        }
    }

    private static void ProcessType(
        INamedTypeSymbol type,
        INamedTypeSymbol componentAttribute,
        INamedTypeSymbol fieldAttribute,
        List<ComponentModel> discovered,
        SourceProductionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (HasAttribute(type, componentAttribute))
        {
            var members = new List<MemberModel>();
            var hasUnsupportedMember = false;
            foreach (var symbol in type.GetMembers())
            {
                ITypeSymbol? memberType = symbol switch
                {
                    IFieldSymbol { IsStatic: false } field when HasAttribute(field, fieldAttribute) => field.Type,
                    IPropertySymbol { IsStatic: false } property when HasAttribute(property, fieldAttribute) => property.Type,
                    _ => null,
                };
                if (memberType is null)
                {
                    continue;
                }

                if (!TryCreateCodec(memberType, out var codec))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedNetworkedField,
                        symbol.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault() ?? Location.None,
                        symbol.Name,
                        type.ToDisplayString(),
                        memberType.ToDisplayString()));
                    hasUnsupportedMember = true;
                    continue;
                }
                members.Add(new MemberModel(
                    symbol.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    codec));
            }

            var metadataName = GetFullyQualifiedMetadataName(type);
            discovered.Add(new ComponentModel(
                metadataName,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ComputeFnv1A64(metadataName),
                members.OrderBy(static member => member.Name, StringComparer.Ordinal).ToArray(),
                type.Locations.FirstOrDefault() ?? Location.None,
                hasUnsupportedMember));
        }

        foreach (var nested in type.GetTypeMembers())
        {
            ProcessType(nested, componentAttribute, fieldAttribute, discovered, context);
        }
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute) =>
        symbol.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));

    private static bool TryCreateCodec(ITypeSymbol type, out CodecKind codec)
    {
        codec = type.SpecialType switch
        {
            SpecialType.System_Single => CodecKind.Float,
            SpecialType.System_Int32 => CodecKind.Int,
            SpecialType.System_Int64 => CodecKind.Long,
            SpecialType.System_Boolean => CodecKind.Bool,
            SpecialType.System_String => CodecKind.String,
            SpecialType.System_Byte => CodecKind.Byte,
            SpecialType.System_UInt16 => CodecKind.UShort,
            SpecialType.System_Double => CodecKind.Double,
            _ => CodecKind.Unsupported,
        };
        if (codec != CodecKind.Unsupported)
        {
            return true;
        }

        codec = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) switch
        {
            "global::System.Drawing.Color" => CodecKind.Color,
            "global::System.Numerics.Vector2" => CodecKind.NumericsVector2,
            "global::System.Numerics.Vector3" => CodecKind.NumericsVector3,
            "global::OpenTK.Mathematics.Vector2" => CodecKind.OpenTkVector2,
            "global::OpenTK.Mathematics.Vector2d" => CodecKind.OpenTkVector2d,
            _ => CodecKind.Unsupported,
        };
        return codec != CodecKind.Unsupported;
    }

    private static string GenerateSource(IReadOnlyList<ComponentModel> components)
    {
        var poolDeclarations = new StringBuilder();
        var componentConstants = new StringBuilder();
        var writeBlocks = new StringBuilder();
        var applyBlocks = new StringBuilder();
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            componentConstants.AppendLine($"        private const ulong ComponentId{index} = 0x{component.Id:X16}UL;");
            poolDeclarations.AppendLine($"            var componentPool{index} = world.GetPool<{component.TypeName}>();");
            writeBlocks.AppendLine($"                var hasComponent{index} = componentPool{index}.Has(entityId);");
            writeBlocks.AppendLine($"                writer.Put(hasComponent{index});");
            writeBlocks.AppendLine($"                if (hasComponent{index})");
            writeBlocks.AppendLine("                {");
            writeBlocks.AppendLine($"                    ref readonly var component = ref componentPool{index}.Get(entityId);");
            foreach (var member in component.Members)
            {
                AppendWrite(writeBlocks, member);
            }
            writeBlocks.AppendLine("                }");

            applyBlocks.AppendLine("                if (reader.GetBool())");
            applyBlocks.AppendLine("                {");
            applyBlocks.AppendLine($"                    ref var component = ref componentPool{index}.TryAddOrGet(localEntityId);");
            foreach (var member in component.Members)
            {
                AppendRead(applyBlocks, member);
            }
            applyBlocks.AppendLine("                }");
            applyBlocks.AppendLine("                else");
            applyBlocks.AppendLine("                {");
            applyBlocks.AppendLine($"                    componentPool{index}.TryDel(localEntityId);");
            applyBlocks.AppendLine("                }");
        }

        var schemaHash = ComputeSchemaHash(components);
        return $$"""
            // <auto-generated/>
            #nullable enable
            using System;
            using System.Collections.Generic;
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.Network.Core;

            namespace Karpik.Engine.Generated
            {
                public sealed class NetworkSnapshotRegistry
                {
                    public const long ProtocolSchemaHash = unchecked((long)0x{{schemaHash:X16}}UL);
            {{componentConstants}}

                    private sealed class NetworkEntityAspect : EcsAspect
                    {
                        public EcsPool<NetworkId> NetworkId = Inc;
                    }

                    private readonly Dictionary<int, int> _networkToEntity = new Dictionary<int, int>();

                    public void WriteSnapshot(EcsWorld world, IWriter writer, ReadOnlySpan<int> destroyedNetworkIds)
                    {
                        writer.Put(destroyedNetworkIds.Length);
                        for (var destroyedIndex = 0; destroyedIndex < destroyedNetworkIds.Length; destroyedIndex++)
                        {
                            writer.Put(destroyedNetworkIds[destroyedIndex]);
                        }

                        var entities = world.Where(out NetworkEntityAspect aspect);
                        writer.Put(entities.Count);
            {{poolDeclarations}}                for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
                        {
                            var entityId = entities[entityIndex];
                            writer.Put(aspect.NetworkId.Get(entityId).Id);
            {{writeBlocks}}                }
                    }

                    public void ApplySnapshot(EcsWorld world, IReader reader)
                    {
                        EnsureClientCache(world);
                        var destroyedCount = reader.GetInt();
                        for (var destroyedIndex = 0; destroyedIndex < destroyedCount; destroyedIndex++)
                        {
                            var networkId = reader.GetInt();
                            if (_networkToEntity.TryGetValue(networkId, out var entityId))
                            {
                                if (world.GetEntityLong(entityId).IsAlive)
                                {
                                    world.DelEntity(entityId);
                                }
                                _networkToEntity.Remove(networkId);
                            }
                        }

                        var entityCount = reader.GetInt();
                        var networkIdPool = world.GetPool<NetworkId>();
            {{poolDeclarations}}                for (var entityIndex = 0; entityIndex < entityCount; entityIndex++)
                        {
                            var networkId = reader.GetInt();
                            if (!_networkToEntity.TryGetValue(networkId, out var localEntityId))
                            {
                                localEntityId = world.NewEntity();
                                networkIdPool.Add(localEntityId).Id = networkId;
                                _networkToEntity.Add(networkId, localEntityId);
                            }
            {{applyBlocks}}                }
                    }

                    public void ClearClientCache() => _networkToEntity.Clear();

                    private void EnsureClientCache(EcsWorld world)
                    {
                        if (_networkToEntity.Count != 0)
                        {
                            return;
                        }

                        var entities = world.Where(out NetworkEntityAspect aspect);
                        for (var index = 0; index < entities.Count; index++)
                        {
                            var entityId = entities[index];
                            var networkId = aspect.NetworkId.Get(entityId).Id;
                            if (!_networkToEntity.ContainsKey(networkId))
                            {
                                _networkToEntity.Add(networkId, entityId);
                            }
                        }
                    }
                }
            }
            """;
    }

    private static void AppendWrite(StringBuilder builder, MemberModel member)
    {
        var value = $"component.{member.Name}";
        switch (member.Codec)
        {
            case CodecKind.NumericsVector2:
            case CodecKind.OpenTkVector2:
            case CodecKind.OpenTkVector2d:
                builder.AppendLine($"                    writer.Put({value}.X);");
                builder.AppendLine($"                    writer.Put({value}.Y);");
                break;
            case CodecKind.NumericsVector3:
                builder.AppendLine($"                    writer.Put({value}.X);");
                builder.AppendLine($"                    writer.Put({value}.Y);");
                builder.AppendLine($"                    writer.Put({value}.Z);");
                break;
            default:
                builder.AppendLine($"                    writer.Put({value});");
                break;
        }
    }

    private static void AppendRead(StringBuilder builder, MemberModel member)
    {
        var read = member.Codec switch
        {
            CodecKind.Float => "reader.GetFloat()",
            CodecKind.Int => "reader.GetInt()",
            CodecKind.Long => "reader.GetLong()",
            CodecKind.Bool => "reader.GetBool()",
            CodecKind.String => "reader.GetString()",
            CodecKind.Byte => "reader.GetByte()",
            CodecKind.UShort => "reader.GetUShort()",
            CodecKind.Double => "reader.GetDouble()",
            CodecKind.Color => "reader.GetColor()",
            CodecKind.NumericsVector2 => "new global::System.Numerics.Vector2(reader.GetFloat(), reader.GetFloat())",
            CodecKind.NumericsVector3 => "new global::System.Numerics.Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat())",
            CodecKind.OpenTkVector2 => "new global::OpenTK.Mathematics.Vector2(reader.GetFloat(), reader.GetFloat())",
            CodecKind.OpenTkVector2d => "new global::OpenTK.Mathematics.Vector2d(reader.GetDouble(), reader.GetDouble())",
            _ => throw new InvalidOperationException("Unsupported codec reached source generation."),
        };
        builder.AppendLine($"                    component.{member.Name} = {read};");
    }

    private static ulong ComputeSchemaHash(IReadOnlyList<ComponentModel> components)
    {
        var builder = new StringBuilder();
        foreach (var component in components)
        {
            builder.Append(component.Id.ToString("X16", CultureInfo.InvariantCulture));
            builder.Append('|').Append(component.MetadataName);
            foreach (var member in component.Members)
            {
                builder.Append('|').Append(member.Name).Append(':').Append(member.TypeName);
            }
            builder.Append(';');
        }
        return ComputeFnv1A64(builder.ToString());
    }

    private static ulong ComputeFnv1A64(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var bytes = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
        var hash = offsetBasis;
        foreach (var valueByte in bytes)
        {
            hash ^= valueByte;
            hash *= prime;
        }
        return hash;
    }

    private static string GetFullyQualifiedMetadataName(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }
        var namespaceName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString() + ".";
        return namespaceName + string.Join("+", typeNames);
    }

    private enum CodecKind
    {
        Unsupported, Float, Int, Long, Bool, String, Byte, UShort, Double, Color,
        NumericsVector2, NumericsVector3, OpenTkVector2, OpenTkVector2d,
    }

    private sealed record MemberModel(string Name, string TypeName, CodecKind Codec);
    private sealed record ComponentModel(
        string MetadataName,
        string TypeName,
        ulong Id,
        IReadOnlyList<MemberModel> Members,
        Location Location,
        bool HasUnsupportedMember);
}
#pragma warning restore RS2008
