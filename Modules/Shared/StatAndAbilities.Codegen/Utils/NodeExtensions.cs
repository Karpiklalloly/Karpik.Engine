using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MoveReplace;

public static class NodeExtensions
{
    public static IEnumerable<T> All<T>(this SyntaxNode root)
    {
        return root.DescendantNodes().OfType<T>();
    }
    
    public static IEnumerable<SyntaxNode> All(this SyntaxNode root, Type type)
    {
        return root.DescendantNodes().Where(node => node.GetType() == type || node.GetType().IsSubclassOf(type));
    }
    
    public static string Name(this FieldDeclarationSyntax attribute)
    {
        return attribute.Declaration.Variables.First().Identifier.Text;
    }
    
    public static string Name(this PropertyDeclarationSyntax attribute)
    {
        return attribute.Identifier.Text;
    }
    
    public static string Name(this AttributeSyntax attribute)
    {
        return attribute.Name.NormalizeWhitespace().ToFullString();
    }
    
    public static string Value(this AttributeArgumentSyntax ats)
    {
        return ats.Expression.NormalizeWhitespace().ToFullString();
    }
    
    public static string? Name(this AttributeArgumentSyntax ats)
    {
        return ats.NameEquals?.Name.Identifier.Text;
    }
    
    public static bool HasAttribute(this MemberDeclarationSyntax node, string attributeName)
    {
        return node.AttributeLists.Any(x => x.Attributes.Any(y => y.Name() == attributeName));
    }
    
    public static AttributeSyntax GetAttribute(this MemberDeclarationSyntax node, string attributeName)
    {
        return node.AttributeLists
            .SelectMany(e => e.Attributes)
            .First(e => e.Name() == attributeName);
    }

    public static AttributeSyntax AddParameter(this AttributeSyntax attribute, string? name, string value)
    {
        var attributeArgument = SyntaxFactory
            .AttributeArgument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(value)));
        if (name != null)
            attributeArgument =
                attributeArgument.WithNameEquals(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(name)));

        var otherList = new SeparatedSyntaxList<AttributeArgumentSyntax>();
        otherList = otherList.Add(attributeArgument);
        var argumentList = SyntaxFactory.AttributeArgumentList(otherList);
        return SyntaxFactory.Attribute(attribute.Name, argumentList);
    }

    public static string TypeName(this FieldDeclarationSyntax node)
    {
        return node.Type().ToString();
    }

    public static TypeSyntax Type(this FieldDeclarationSyntax node)
    {
        return node.Declaration.Type;
    }

    public static FieldDeclarationSyntax? Find(this SyntaxNode root, FieldDeclarationSyntax field)
    {
        return AttributeAdder.Find(root, field);
    }
    
    public static PropertyDeclarationSyntax? Find(this SyntaxNode root, PropertyDeclarationSyntax property)
    {
        return AttributeAdder.Find(root, property);
    }
}