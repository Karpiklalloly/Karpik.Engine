using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Karpik.StatAndAbilities.Codegen;

public class StructWithStatReceiver : ISyntaxReceiver
{
    public List<StructDeclarationSyntax> Structs { get; } = new();
    
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not StructDeclarationSyntax structDeclaration) return;
        
        Structs.Add(structDeclaration);
    }
}