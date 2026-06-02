using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StaticAnalyzer.ECS;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseWorldCodeFixProvider))]
[Shared]
public sealed class UseWorldCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.UseWorldInsteadOfEcsWorld);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

        if (node.FirstAncestorOrSelf<FieldDeclarationSyntax>() is { } field)
        {
            RegisterFix(context, field.Declaration.Type, diagnostic);
            return;
        }

        if (node.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property)
        {
            RegisterFix(context, property.Type, diagnostic);
        }
    }

    private static void RegisterFix(CodeFixContext context, TypeSyntax typeSyntax, Diagnostic diagnostic)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with wrapped ECS world",
                createChangedDocument: ct => ReplaceTypeAsync(context.Document, typeSyntax, ct),
                equivalenceKey: nameof(UseWorldCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceTypeAsync(
        Document document,
        TypeSyntax oldTypeSyntax,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        var type = semanticModel.GetTypeInfo(oldTypeSyntax, cancellationToken).Type;
        if (type is null || !UseWorldAnalyzer.TryGetReplacement(type, out var replacement))
            return document;

        var (namespaceName, typeName) = SplitQualifiedName(replacement);
        var newTypeSyntax = SyntaxFactory.ParseTypeName(typeName)
            .WithTriviaFrom(oldTypeSyntax);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var newRoot = root.ReplaceNode(oldTypeSyntax, newTypeSyntax);
        newRoot = AddUsingIfMissing(newRoot, namespaceName);

        return document.WithSyntaxRoot(newRoot);
    }

    private static (string NamespaceName, string TypeName) SplitQualifiedName(string fullyQualifiedName)
    {
        var lastDot = fullyQualifiedName.LastIndexOf('.');
        return lastDot < 0
            ? (string.Empty, fullyQualifiedName)
            : (fullyQualifiedName.Substring(0, lastDot), fullyQualifiedName.Substring(lastDot + 1));
    }

    private static SyntaxNode AddUsingIfMissing(SyntaxNode root, string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            return root;

        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        var alreadyImported = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (alreadyImported)
            return root;

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        return compilationUnit.AddUsings(usingDirective);
    }
}
