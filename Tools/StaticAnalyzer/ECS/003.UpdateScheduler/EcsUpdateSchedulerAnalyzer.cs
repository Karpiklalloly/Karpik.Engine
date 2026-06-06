using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

#nullable enable

namespace StaticAnalyzer.ECS;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EcsUpdateSchedulerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        SchedulerDiagnosticDescriptors.OpaqueUpdateAccess,
        SchedulerDiagnosticDescriptors.AccessSummaryContradiction,
        SchedulerDiagnosticDescriptors.InvalidOrderCycle,
        SchedulerDiagnosticDescriptors.MainThreadOnlyUpdateAccess,
        SchedulerDiagnosticDescriptors.UnsupportedManagedComponentSummary,
        SchedulerDiagnosticDescriptors.GeneratedRegistryMissingSystem);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterOperationBlockAction(AnalyzeOperationBlock);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type || !ImplementsSystemUpdate(type))
            return;

        ReportUnsupportedManagedSummaries(context, type);

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IMethodSymbol method)
                ReportUnsupportedManagedSummaries(context, method);
        }
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol method ||
            method.Name != "Update" ||
            method.Parameters.Length != 0 ||
            !ImplementsSystemUpdate(method.ContainingType) ||
            HasAttribute(method.ContainingType, "SequentialSystemAttribute"))
        {
            return;
        }

        var result = new AccessAnalysisResult();
        AddAccessSummaries(method.ContainingType, result.Declared);
        AddAccessSummaries(method, result.Declared);

        var visitedMethods = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var walker = new SchedulerOperationWalker(
            method.ContainingType,
            context.Compilation,
            result,
            visitedMethods,
            context.CancellationToken,
            diagnostic => context.ReportDiagnostic(diagnostic));

        foreach (IOperation operationBlock in context.OperationBlocks)
        {
            walker.Visit(operationBlock);
        }

        foreach (var declared in result.Declared)
        {
            if (declared.Mode != SchedulerAccessMode.Read)
                continue;

            foreach (var inferred in result.Inferred)
            {
                if (inferred.Mode == SchedulerAccessMode.Write &&
                    SymbolEqualityComparer.Default.Equals(declared.ComponentType, inferred.ComponentType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SchedulerDiagnosticDescriptors.AccessSummaryContradiction,
                        method.Locations[0],
                        method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    return;
                }
            }
        }
    }

    private static void ReportUnsupportedManagedSummaries(SymbolAnalysisContext context, ISymbol symbol)
    {
        foreach (var access in GetAccessSummaries(symbol))
        {
            if (!ContainsManagedReferences(access.ComponentType))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                SchedulerDiagnosticDescriptors.UnsupportedManagedComponentSummary,
                access.Attribute is null
                    ? symbol.Locations[0]
                    : GetAttributeLocation(access.Attribute) ?? symbol.Locations[0],
                access.ComponentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AddAccessSummaries(ISymbol symbol, ICollection<SchedulerAccess> target)
    {
        foreach (var access in GetAccessSummaries(symbol))
            target.Add(access);
    }

    private static IEnumerable<SchedulerAccess> GetAccessSummaries(ISymbol symbol)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not INamedTypeSymbol attributeType ||
                attributeType.TypeArguments.Length != 1)
            {
                continue;
            }

            SchedulerAccessMode? mode = attributeType.Name switch
            {
                "ReadsAttribute" => SchedulerAccessMode.Read,
                "WritesAttribute" => SchedulerAccessMode.Write,
                _ => null
            };

            if (mode is null)
                continue;

            yield return new SchedulerAccess(attributeType.TypeArguments[0], mode.Value, attribute);
        }
    }

    private static bool HasAccessSummary(ISymbol symbol)
    {
        return GetAccessSummaries(symbol).Any();
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == attributeName)
                return true;
        }

        return false;
    }

    private static Location? GetAttributeLocation(AttributeData attribute)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
    }

    private static bool ImplementsSystemUpdate(INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol iface in type.AllInterfaces)
        {
            if (iface.Name == "ISystemUpdate")
                return true;
        }

        return false;
    }

    private static bool TryGetPoolAccess(ITypeSymbol? type, out SchedulerAccess access)
    {
        access = default;

        if (type is not INamedTypeSymbol namedType || namedType.TypeArguments.Length != 1)
            return false;

        SchedulerAccessMode? mode = namedType.Name switch
        {
            "EcsPool" => SchedulerAccessMode.Write,
            "EcsReadonlyPool" => SchedulerAccessMode.Read,
            _ => null
        };

        if (mode is null || !IsDragonEcsNamespace(namedType.ContainingNamespace))
            return false;

        access = new SchedulerAccess(namedType.TypeArguments[0], mode.Value, null);
        return true;
    }

    private static bool ContainsManagedReferences(ITypeSymbol type)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return ContainsManagedReferences(type, visited);
    }

    private static bool ContainsManagedReferences(ITypeSymbol type, HashSet<ISymbol> visited)
    {
        if (type.IsReferenceType)
            return true;

        if (type is not INamedTypeSymbol namedType || namedType.TypeKind != TypeKind.Struct)
            return false;

        if (!visited.Add(namedType))
            return false;

        foreach (IFieldSymbol field in namedType.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsStatic)
                continue;

            if (ContainsManagedReferences(field.Type, visited))
                return true;
        }

        return false;
    }

    private static bool IsSourceMethod(IMethodSymbol method)
    {
        return method.DeclaringSyntaxReferences.Length > 0;
    }

    private static bool TryGetMethodOperation(
        IMethodSymbol method,
        Compilation compilation,
        CancellationToken cancellationToken,
        out IOperation? operation)
    {
        operation = null;

        SyntaxReference? syntaxReference = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference is null)
            return false;

        SyntaxNode syntax = syntaxReference.GetSyntax(cancellationToken);
#pragma warning disable RS1030
        SemanticModel semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
#pragma warning restore RS1030

        operation = syntax switch
        {
            MethodDeclarationSyntax { Body: not null } methodSyntax
                => semanticModel.GetOperation(methodSyntax.Body, cancellationToken),
            MethodDeclarationSyntax { ExpressionBody.Expression: { } expression }
                => semanticModel.GetOperation(expression, cancellationToken),
            LocalFunctionStatementSyntax { Body: not null } localFunction
                => semanticModel.GetOperation(localFunction.Body, cancellationToken),
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression }
                => semanticModel.GetOperation(expression, cancellationToken),
            _ => null
        };

        return operation is not null;
    }

    private static bool IsEcsCapableType(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (TryGetPoolAccess(type, out _))
            return true;

        if (type is INamedTypeSymbol namedType &&
            IsDragonEcsNamespace(namedType.ContainingNamespace) &&
            (namedType.Name.StartsWith("Ecs", StringComparison.Ordinal) || namedType.Name.Contains("Pool")))
        {
            return true;
        }

        foreach (INamedTypeSymbol iface in type.AllInterfaces)
        {
            if (iface.Name is "IEcsPool" or "IEcsReadonlyPool" or "IEcsPoolImplementation")
                return true;
        }

        return false;
    }

    private static bool IsDragonEcsNamespace(INamespaceSymbol? namespaceSymbol)
    {
        return namespaceSymbol?.ToDisplayString() == "DCFApixels.DragonECS";
    }

    private readonly struct SchedulerAccess
    {
        public SchedulerAccess(ITypeSymbol componentType, SchedulerAccessMode mode, AttributeData? attribute)
        {
            ComponentType = componentType;
            Mode = mode;
            Attribute = attribute;
        }

        public ITypeSymbol ComponentType { get; }
        public SchedulerAccessMode Mode { get; }
        public AttributeData? Attribute { get; }
    }

    private enum SchedulerAccessMode
    {
        Read,
        Write
    }

    private sealed class AccessAnalysisResult
    {
        public List<SchedulerAccess> Declared { get; } = new();
        public List<SchedulerAccess> Inferred { get; } = new();
    }

    private sealed class SchedulerOperationWalker : OperationWalker
    {
        private readonly INamedTypeSymbol _systemType;
        private readonly Compilation _compilation;
        private readonly AccessAnalysisResult _result;
        private readonly HashSet<ISymbol> _visitedMethods;
        private readonly CancellationToken _cancellationToken;
        private readonly Action<Diagnostic> _reportDiagnostic;

        public SchedulerOperationWalker(
            INamedTypeSymbol systemType,
            Compilation compilation,
            AccessAnalysisResult result,
            HashSet<ISymbol> visitedMethods,
            CancellationToken cancellationToken,
            Action<Diagnostic> reportDiagnostic)
        {
            _systemType = systemType;
            _compilation = compilation;
            _result = result;
            _visitedMethods = visitedMethods;
            _cancellationToken = cancellationToken;
            _reportDiagnostic = reportDiagnostic;
        }

        public override void VisitInvocation(IInvocationOperation operation)
        {
            if (IsMainThreadOnly(operation.TargetMethod))
            {
                _reportDiagnostic(Diagnostic.Create(
                    SchedulerDiagnosticDescriptors.MainThreadOnlyUpdateAccess,
                    operation.Syntax.GetLocation(),
                    operation.TargetMethod.Name));
                base.VisitInvocation(operation);
                return;
            }

            if (operation.TargetMethod.MethodKind == MethodKind.DelegateInvoke)
            {
                ReportOpaque(operation.Syntax.GetLocation(), "delegate invocation");
                base.VisitInvocation(operation);
                return;
            }

            if (TryGetPoolAccess(operation.TargetMethod.ContainingType, out SchedulerAccess access))
            {
                _result.Inferred.Add(access);
                base.VisitInvocation(operation);
                return;
            }

            if (TryGetWorldFacadeAccess(operation.TargetMethod, out access))
            {
                _result.Inferred.Add(access);
                base.VisitInvocation(operation);
                return;
            }

            if (HasAccessSummary(operation.TargetMethod))
            {
                AddAccessSummaries(operation.TargetMethod, _result.Declared);
                AddAccessSummaries(operation.TargetMethod, _result.Inferred);

                if (!IsUnresolvedDispatch(operation.TargetMethod) &&
                    IsSourceMethod(operation.TargetMethod))
                {
                    VisitSourceMethod(operation.TargetMethod, operation.Syntax.GetLocation());
                }

                base.VisitInvocation(operation);
                return;
            }

            if (IsUnresolvedDispatch(operation.TargetMethod))
            {
                ReportOpaque(operation.Syntax.GetLocation(), operation.TargetMethod.Name);
                base.VisitInvocation(operation);
                return;
            }

            if (IsSourceMethod(operation.TargetMethod))
            {
                VisitSourceMethod(operation.TargetMethod, operation.Syntax.GetLocation());
                base.VisitInvocation(operation);
                return;
            }

            if (!IsPureAllowedExternalCall(operation.TargetMethod))
            {
                ReportOpaque(operation.Syntax.GetLocation(), operation.TargetMethod.Name);
                base.VisitInvocation(operation);
                return;
            }

            if (IsEcsCapableCall(operation))
            {
                ReportOpaque(operation.Syntax.GetLocation(), operation.TargetMethod.Name);
            }

            base.VisitInvocation(operation);
        }

        private static bool IsPureAllowedExternalCall(IMethodSymbol method)
        {
            INamedTypeSymbol? containingType = method.ContainingType;
            if (containingType is null)
                return false;

            string containingTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return containingTypeName is "global::System.Math" or "global::System.MathF";
        }

        private static bool TryGetWorldFacadeAccess(IMethodSymbol method, out SchedulerAccess access)
        {
            access = default;

            if (method.TypeArguments.Length != 1)
                return false;

            if (IsDragonGetPoolMethod(method))
            {
                access = new SchedulerAccess(method.TypeArguments[0], SchedulerAccessMode.Write, null);
                return true;
            }

            if (method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
                "global::DragonExtensions.World")
            {
                return false;
            }

            SchedulerAccessMode? mode = method.Name switch
            {
                "Has" or "TryGet" => SchedulerAccessMode.Read,
                "Get" or "Add" or "Set" or "Del" or "Event" => SchedulerAccessMode.Write,
                _ => null
            };

            if (mode is null)
                return false;

            access = new SchedulerAccess(method.TypeArguments[0], mode.Value, null);
            return true;
        }

        private static bool IsDragonGetPoolMethod(IMethodSymbol method)
        {
            if (method.Name is not ("GetPool" or "GetPoolUnchecked"))
                return false;

            return method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                   "global::DCFApixels.DragonECS.EcsPoolExtensions";
        }

        private static bool IsUnresolvedDispatch(IMethodSymbol method)
        {
            if (method.ContainingType.TypeKind == TypeKind.Interface)
                return true;

            if (method.IsAbstract)
                return true;

            if (method.IsVirtual || method.IsOverride)
            {
                return !method.IsSealed && !method.ContainingType.IsSealed;
            }

            return false;
        }

        private static bool IsMainThreadOnly(IMethodSymbol method)
        {
            return HasAttribute(method, "MainThreadOnlyAttribute") ||
                   HasAttribute(method.ContainingType, "MainThreadOnlyAttribute");
        }

        public override void VisitDynamicInvocation(IDynamicInvocationOperation operation)
        {
            ReportOpaque(operation.Syntax.GetLocation(), "dynamic invocation");
            base.VisitDynamicInvocation(operation);
        }

        public override void VisitTypeOf(ITypeOfOperation operation)
        {
            ReportOpaque(operation.Syntax.GetLocation(), "typeof");
            base.VisitTypeOf(operation);
        }

        private void VisitSourceMethod(IMethodSymbol method, Location fallbackLocation)
        {
            if (!_visitedMethods.Add(method))
                return;

            if (!TryGetMethodOperation(method, _compilation, _cancellationToken, out IOperation? operation) ||
                operation is null)
            {
                ReportOpaque(fallbackLocation, method.Name);
                return;
            }

            Visit(operation);
        }

        private bool IsEcsCapableCall(IInvocationOperation operation)
        {
            if (IsEcsCapableType(operation.Instance?.Type))
                return true;

            foreach (IArgumentOperation argument in operation.Arguments)
            {
                if (IsEcsCapableType(argument.Value.Type))
                    return true;
            }

            return false;
        }

        private void ReportOpaque(Location location, string displayName)
        {
            _reportDiagnostic(Diagnostic.Create(
                SchedulerDiagnosticDescriptors.OpaqueUpdateAccess,
                location,
                displayName));
        }
    }
}
