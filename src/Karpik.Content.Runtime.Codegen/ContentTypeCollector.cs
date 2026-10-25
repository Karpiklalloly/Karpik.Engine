using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Karpik.Content.Codegen;

internal static class ContentTypeCollector
{
    public static IReadOnlyDictionary<string, INamedTypeSymbol> Collect(Compilation compilation)
    {
        var map = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var attributeSymbol = compilation.GetTypeByMetadataName("Karpik.Content.Runtime.ContentTypeAttribute");
        if (attributeSymbol == null)
        {
            return map;
        }

        ProcessNamespace(compilation.Assembly.GlobalNamespace, attributeSymbol, map);
        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ProcessNamespace(referenced.GlobalNamespace, attributeSymbol, map);
        }

        return map;
    }

    private static void ProcessNamespace(INamespaceSymbol ns, INamedTypeSymbol attribute, Dictionary<string, INamedTypeSymbol> map)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ProcessType(type, attribute, map);
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            ProcessNamespace(child, attribute, map);
        }
    }

    private static void ProcessType(INamedTypeSymbol type, INamedTypeSymbol attribute, Dictionary<string, INamedTypeSymbol> map)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass != null && SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute))
            {
                string? declaredType = null;
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s)
                {
                    declaredType = s;
                }
                else if (attr.NamedArguments.Length > 0)
                {
                    foreach (var kv in attr.NamedArguments)
                    {
                        if (kv.Key == "DeclaredType" && kv.Value.Value is string ns2)
                        {
                            declaredType = ns2;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(declaredType) && !map.ContainsKey(declaredType))
                {
                    map[declaredType] = type;
                }

                break;
            }
        }

        foreach (var nested in type.GetTypeMembers())
        {
            ProcessType(nested, attribute, map);
        }
    }
}
