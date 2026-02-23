using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace StaticAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StaticReferenceAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Performance";
        private const string ProhibitedDirectoriesConfig = "prohibited_source_directories";

        public const string DirectReferenceId = "SA001";
        private static readonly DiagnosticDescriptor RuleSA001 = new DiagnosticDescriptor(DirectReferenceId, "Static reference to unloadable type", "Static field/property '{0}' has a type '{1}' from an unloadable directory '{2}'", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public const string IndirectReferenceId = "SA002";
        private static readonly DiagnosticDescriptor RuleSA002 = new DiagnosticDescriptor(IndirectReferenceId, "Indirect static reference to unloadable type", "Static field/property '{0}' is initialized with a reference to type '{1}' from an unloadable directory '{2}'", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
        
        public const string TypeContainerId = "SA003";
        private static readonly DiagnosticDescriptor RuleSA003 = new DiagnosticDescriptor(TypeContainerId, "Potentially unsafe static reference via System.Type container", "Static field/property '{0}' has a generic argument '{1}' which contains a 'System.Type' field. This can cause memory leaks if types from unloadable directories are stored.", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(RuleSA001, RuleSA002, RuleSA003);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Field, SymbolKind.Property);
            context.RegisterSyntaxNodeAction(AnalyzeInitializer, SyntaxKind.VariableDeclarator);
        }

        private void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            var symbol = context.Symbol;
            if (!symbol.IsStatic) return;

            var prohibitedDirectories = GetProhibitedDirectories(context.Options, symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree);
            var solutionDir = GetSolutionDirFromSymbol(symbol);

            ITypeSymbol typeToCheck = null;
            if (symbol is IFieldSymbol fieldSymbol)
            {
                typeToCheck = fieldSymbol.Type;
            }
            else if (symbol is IPropertySymbol propertySymbol)
            {
                typeToCheck = propertySymbol.Type;
            }

            if (typeToCheck != null)
            {
                CheckType(typeToCheck, symbol, prohibitedDirectories, context.ReportDiagnostic, RuleSA001, solutionDir, new HashSet<ITypeSymbol>());
            }
        }

        private void AnalyzeInitializer(SyntaxNodeAnalysisContext context)
        {
            var declarator = (VariableDeclaratorSyntax)context.Node;
            var fieldSymbol = context.SemanticModel.GetDeclaredSymbol(declarator) as IFieldSymbol;

            if (fieldSymbol == null || !fieldSymbol.IsStatic || declarator.Initializer == null) return;
            
            var prohibitedDirectories = GetProhibitedDirectories(context.Options, declarator.SyntaxTree);
            var solutionDir = GetSolutionDirFromSymbol(fieldSymbol);

            foreach (var node in declarator.Initializer.Value.DescendantNodesAndSelf())
            {
                ISymbol referencedSymbol = null;
                if (node is TypeOfExpressionSyntax typeOfExp)
                {
                    var typeInfo = context.SemanticModel.GetTypeInfo(typeOfExp.Type);
                    referencedSymbol = typeInfo.Type;
                }
                else if (node is IdentifierNameSyntax identifierName)
                {
                     referencedSymbol = context.SemanticModel.GetSymbolInfo(identifierName).Symbol;
                }

                if (referencedSymbol != null)
                {
                    var typeToCheck = referencedSymbol is ITypeSymbol ts ? ts : referencedSymbol.ContainingType;
                    if (IsTypeFromProhibitedDirectory(typeToCheck, prohibitedDirectories, solutionDir, out var matchedDir))
                    {
                        var diagnostic = Diagnostic.Create(RuleSA002, fieldSymbol.Locations[0], fieldSymbol.Name, typeToCheck.ToDisplayString(), matchedDir);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        private void CheckType(ITypeSymbol typeSymbol, ISymbol ownerSymbol, List<string> prohibitedDirectories, Action<Diagnostic> reportAction, DiagnosticDescriptor rule, string solutionDir, HashSet<ITypeSymbol> visited)
        {
            if (typeSymbol == null || !visited.Add(typeSymbol)) return;

            if (IsTypeFromProhibitedDirectory(typeSymbol, prohibitedDirectories, solutionDir, out var matchedDir))
            {
                var diagnostic = Diagnostic.Create(rule, ownerSymbol.Locations[0], ownerSymbol.Name, typeSymbol.ToDisplayString(), matchedDir);
                reportAction(diagnostic);
            }

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
            {
                if (namedTypeSymbol.IsGenericType)
                {
                    foreach (var typeArgument in namedTypeSymbol.TypeArguments)
                    {
                        // Recurse on generic arguments
                        CheckType(typeArgument, ownerSymbol, prohibitedDirectories, reportAction, rule, solutionDir, visited);
                    }
                }

                // Check fields of the type for System.Type to flag potential containers
                foreach (var member in typeSymbol.GetMembers())
                {
                    if (member is IFieldSymbol field && !field.IsStatic)
                    {
                        if (field.Type.SpecialType == SpecialType.System_Object || 
                            (field.Type as INamedTypeSymbol)?.IsUnboundGenericType == false && field.Type.ToDisplayString() == "System.Type")
                        {
                            var diagnostic = Diagnostic.Create(RuleSA003, ownerSymbol.Locations[0], ownerSymbol.Name, typeSymbol.ToDisplayString());
                            reportAction(diagnostic);
                            break; // Report only once per container type
                        }
                    }
                }
            }
        }

        private bool IsTypeFromProhibitedDirectory(ITypeSymbol typeSymbol, List<string> prohibitedDirectories, string solutionDir, out string matchedDirectory)
        {
            matchedDirectory = null;
            if (typeSymbol == null || typeSymbol.Locations.IsEmpty || string.IsNullOrEmpty(solutionDir)) return false;

            var typeLocation = typeSymbol.Locations.FirstOrDefault(loc => loc.IsInSource && loc.SourceTree != null);
            if (typeLocation == null) return false;

            var filePath = typeLocation.SourceTree.FilePath;
            if (string.IsNullOrEmpty(filePath)) return false;

            foreach (var prohibitedDir in prohibitedDirectories)
            {
                var normalizedFilePath = Path.GetFullPath(filePath).Replace('\\', '/');
                var normalizedProhibitedDir = Path.GetFullPath(Path.Combine(solutionDir, prohibitedDir)).Replace('\\', '/');

                if (normalizedFilePath.StartsWith(normalizedProhibitedDir, StringComparison.OrdinalIgnoreCase))
                {
                    matchedDirectory = prohibitedDir;
                    return true;
                }
            }
            return false;
        }

        private List<string> GetProhibitedDirectories(AnalyzerOptions options, SyntaxTree syntaxTree)
        {
            if (syntaxTree == null) return new List<string> { "Modules", "MyGame" };
            var config = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            if (!config.TryGetValue($"dotnet_diagnostic.{DirectReferenceId}.{ProhibitedDirectoriesConfig}", out var prohibitedDirsString))
            {
                prohibitedDirsString = "Modules,MyGame"; // Default value
            }
            return prohibitedDirsString.Split(',').Select(d => d.Trim()).ToList();
        }
        
        private string GetSolutionDirFromSymbol(ISymbol symbol)
        {
            var projectPath = symbol.ContainingAssembly?.Locations.FirstOrDefault(loc => loc.IsInSource)?.SourceTree?.FilePath;
            if (projectPath == null) return string.Empty;

            var directory = new DirectoryInfo(Path.GetDirectoryName(projectPath));
            while (directory != null && !directory.GetFiles("*.slnx").Any())
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? string.Empty;
        }
    }
}
