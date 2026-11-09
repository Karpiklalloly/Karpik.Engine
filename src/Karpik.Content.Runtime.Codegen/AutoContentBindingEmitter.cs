#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Karpik.Content.Codegen;

public sealed partial class ContentCodegenGenerator
{
    private static readonly DiagnosticDescriptor InvalidBindingDeclaration = new(
        "KCO305", "Invalid automatic Content binding", "{0}", "Karpik.Content.Codegen",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static void GenerateBindings(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> owners,
        List<FieldInfo> fields, Dictionary<string, string> typeMap, string side, bool enabled, string excludedTypes)
    {
        void Error(Location? location, string message) =>
            context.ReportDiagnostic(Diagnostic.Create(InvalidBindingDeclaration, location, message));

        if (owners.Length != 1)
        {
            Error(owners[0].Locations.FirstOrDefault(), "Declare one automatic Content binding owner per project.");
            return;
        }

        INamedTypeSymbol owner = owners[0];
        if (owner.ContainingType != null || owner.Arity != 0 || owner.IsStatic
            || owner.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax(context.CancellationToken) is not ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            Error(owner.Locations.FirstOrDefault(), "The automatic Content binding owner must be a top-level, non-generic partial class.");
            return;
        }

        var providers = new Dictionary<string, (IMethodSymbol Method, string Type)>(StringComparer.Ordinal);
        foreach (var method in owner.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "Karpik.Content.Runtime.AutoBindJsonAttribute")
                    continue;
                var logical = attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string : null;
                if (logical == null || method.Arity != 0 || method.Parameters.Length != 0
                    || method.ReturnType is not INamedTypeSymbol { IsTupleType: true } tuple
                    || tuple.TupleElements.Length != 2
                    || tuple.TupleElements[0].Type is not INamedTypeSymbol metadata
                    || metadata.OriginalDefinition.ToDisplayString() != "System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>"
                    || !SymbolEqualityComparer.Default.Equals(metadata.TypeArguments[0], tuple.TupleElements[1].Type)
                    || !fields.Any(f => f.Entry.LogicalName == logical && f.Entry.DeclaredType == "raw-json")
                    || providers.ContainsKey(logical))
                {
                    if (enabled) Error(method.Locations.FirstOrDefault(),
                        "AutoBindJson requires a unique raw-json logical name and a parameterless (JsonTypeInfo<T>, T) provider.");
                    continue;
                }

                providers.Add(logical, (method, metadata.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        var exclusions = new HashSet<string>(
            excludedTypes.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
            StringComparer.Ordinal);
        var members = new List<(FieldInfo Field, string Type, IMethodSymbol? Provider, bool Register)>();
        foreach (var field in fields)
        {
            var entry = field.Entry;
            if (entry.Targets.Length != 0 && !string.IsNullOrEmpty(side)
                && !entry.Targets.Contains(side, StringComparer.OrdinalIgnoreCase))
                continue;
            if (entry.DeclaredType == "texture" && !string.Equals(side, "Client", StringComparison.OrdinalIgnoreCase))
                continue;
            providers.TryGetValue(entry.LogicalName, out var json);
            string? type = entry.DeclaredType == "raw-json"
                ? json.Type ?? "global::Karpik.Content.Runtime.RawJsonPayload"
                : typeMap.TryGetValue(entry.DeclaredType, out var mapped) ? mapped : null;
            if (type == null) continue;

            if (enabled && entry.AutoBind is not (JsonValueKind.Undefined or JsonValueKind.True or JsonValueKind.False))
                Error(owner.Locations.FirstOrDefault(), $"Asset '{entry.LogicalName}' has an autoBind value that must be boolean.");

            members.Add((field, type, json.Method,
                enabled && entry.AutoBind != JsonValueKind.False && !exclusions.Contains(entry.DeclaredType)));
        }

        context.AddSource("AutoContentBindings.g.cs", SourceText.From(EmitBindingOwner(owner, members), Encoding.UTF8));
    }

    private static string EmitBindingOwner(INamedTypeSymbol owner,
        List<(FieldInfo Field, string Type, IMethodSymbol? Provider, bool Register)> members)
    {
        var text = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!owner.ContainingNamespace.IsGlobalNamespace)
            text.AppendLine($"namespace {owner.ContainingNamespace.ToDisplayString()};");
        text.AppendLine($"partial class @{owner.Name}");
        text.AppendLine("{");
        var selected = members.Where(m => m.Register).ToArray();
        if (selected.Length > 0)
            text.AppendLine("    private global::Karpik.Content.Runtime.IContentBindings? _generatedBindings;");
        foreach (var item in members)
        {
            string name = item.Field.FieldName;
            string handle = $"global::Karpik.Content.Runtime.ContentBinding<{item.Type}>";
            if (item.Register)
            {
                text.AppendLine($"    private {handle} _auto_{name};");
                text.AppendLine($"    public {handle} {name}Binding => _auto_{name};");
                text.AppendLine($"    public bool TryGet{name}(out {item.Type} value)");
                text.AppendLine("    {");
                text.AppendLine($"        if (_generatedBindings is {{ }} bindings) return bindings.TryGet(_auto_{name}, out value);");
                text.AppendLine("        value = default!;");
                text.AppendLine("        return false;");
                text.AppendLine("    }");
            }
            else
            {
                text.AppendLine($"    public {handle} {name}Binding => default;");
                text.AppendLine($"    public bool TryGet{name}(out {item.Type} value) {{ value = default!; return false; }}");
            }
        }

        text.AppendLine("    private void InitializeGeneratedBindings(global::Karpik.Content.Runtime.IContentBindings bindings)");
        text.AppendLine("    {");
        if (selected.Length > 0)
        {
            text.AppendLine("        if (_generatedBindings != null) return;");
            text.AppendLine("        _generatedBindings = bindings;");
            text.AppendLine("        string asset = string.Empty;");
            text.AppendLine("        try");
            text.AppendLine("        {");
            foreach (var item in selected)
            {
                string name = item.Field.FieldName;
                string id = $"global::Karpik.Content.Generated.ContentRefs.{name}.Id";
                text.AppendLine($"            asset = \"{EscapeString(item.Field.Entry.LogicalName)}\";");
                if (item.Provider == null)
                    text.AppendLine($"            _auto_{name} = bindings.Bind<{item.Type}>({id});");
                else
                {
                    text.AppendLine($"            var metadata_{name} = @{item.Provider.Name}();");
                    text.AppendLine($"            _auto_{name} = bindings.BindJson({id}, metadata_{name}.Item1, metadata_{name}.Item2);");
                }
            }
            text.AppendLine("        }");
            text.AppendLine("        catch (global::System.Exception error)");
            text.AppendLine("        {");
            text.AppendLine("            ReleaseGeneratedBindings();");
            text.AppendLine($"            throw new global::System.InvalidOperationException($\"Failed to register automatic Content binding '{{asset}}' ({selected.Length} selected). Check the binding capacity and asset configuration.\", error);");
            text.AppendLine("        }");
        }
        text.AppendLine("    }");

        text.AppendLine("    private void ReleaseGeneratedBindings()");
        text.AppendLine("    {");
        if (selected.Length > 0)
        {
            text.AppendLine("        if (_generatedBindings is not { } bindings) return;");
            text.AppendLine("        _generatedBindings = null;");
            foreach (var item in selected)
            {
                string field = "_auto_" + item.Field.FieldName;
                text.AppendLine($"        if ({field} != default) bindings.Release({field});");
                text.AppendLine($"        {field} = default;");
            }
        }
        text.AppendLine("    }");
        text.AppendLine("}");
        return text.ToString();
    }
}
