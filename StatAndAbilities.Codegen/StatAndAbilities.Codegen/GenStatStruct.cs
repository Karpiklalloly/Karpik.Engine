using System.Collections.Generic;
using System.Text;
using System.Threading;
using Karpik.StatAndAbilities.Codegen.Attributes; // Required for CancellationToken
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Karpik.StatAndAbilities.Codegen
{
    [Generator]
    public class GenStatStructIncremental : IIncrementalGenerator
    {
        public const string Stat = StatAttribute.AttributeName;
        public const string RangeStat = RangeStatAttribute.AttributeName;
        public const string EzRangeStat = EzRangeStatAttribute.AttributeName;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Шаг 1: Найти все объявления структур с атрибутами
            IncrementalValuesProvider<StructDeclarationSyntax> structDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsStructWithAttributes(node), // Фильтр синтаксиса
                    transform: static (ctx, _) => GetStructDeclaration(ctx)      // Получить узел структуры
                )
                .Where(static m => m is not null)!; // Отфильтровать null (если GetStructDeclaration вернет null)

            // Шаг 2: Объединить с компиляцией и получить семантическую информацию
            IncrementalValuesProvider<(StructDeclarationSyntax Syntax, INamedTypeSymbol Symbol)> structSymbols = structDeclarations
                .Combine(context.CompilationProvider)
                .Select(static (tuple, ct) => GetSemanticTarget(tuple.Left, tuple.Right, ct))
                .Where(static m => m.Symbol is not null)!; // Убедиться, что символ найден

            // Шаг 3: Извлечь данные, необходимые для генерации, и определить тип атрибута
            IncrementalValuesProvider<StructGenerationInfo?> structGenerationInfos = structSymbols
                .Select(static (data, ct) => GetStructGenerationInfo(data.Symbol, ct));

            // Шаг 4: Отфильтровать структуры без нужных атрибутов
             IncrementalValuesProvider<StructGenerationInfo> validStructInfos = structGenerationInfos
                .Where(static m => m is not null)!; // Убрать null значения

            // Шаг 5: Сгенерировать код для каждой валидной структуры
            IncrementalValuesProvider<(string HintName, SourceText Source)> generatedCode = validStructInfos
                .SelectMany(static (info, ct) => GenerateSourceCode(info)); // SelectMany т.к. генератор возвращает List

            // Шаг 6: Добавить сгенерированный код в компиляцию
            context.RegisterSourceOutput(generatedCode, static (spc, source) =>
            {
                spc.AddSource(source.HintName, source.Source);
            });
        }

        // Предикат для CreateSyntaxProvider: проверяет, является ли узел структурой с атрибутами
        private static bool IsStructWithAttributes(SyntaxNode node)
        {
            return node is StructDeclarationSyntax sds && sds.AttributeLists.Count > 0;
        }

        // Трансформация для CreateSyntaxProvider: возвращает узел структуры
        private static StructDeclarationSyntax? GetStructDeclaration(GeneratorSyntaxContext context)
        {
            return context.Node as StructDeclarationSyntax;
        }

        // Получает семантический символ для синтаксического узла структуры
        private static (StructDeclarationSyntax Syntax, INamedTypeSymbol? Symbol) GetSemanticTarget(StructDeclarationSyntax structSyntax, Compilation compilation, CancellationToken ct)
        {
             ct.ThrowIfCancellationRequested();
             var semanticModel = compilation.GetSemanticModel(structSyntax.SyntaxTree);
             var symbol = semanticModel.GetDeclaredSymbol(structSyntax, ct) as INamedTypeSymbol;
             return (structSyntax, symbol);
        }

        // Извлекает информацию, необходимую для генерации, если структура имеет один из целевых атрибутов
        private static StructGenerationInfo? GetStructGenerationInfo(INamedTypeSymbol structSymbol, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string? foundAttributeType = null;

            foreach (var attributeData in structSymbol.GetAttributes())
            {
                string attributeName = attributeData.AttributeClass?.Name ?? string.Empty;
                switch (attributeName)
                {
                    case Stat:
                        foundAttributeType = Stat;
                        break;
                    case RangeStat:
                        foundAttributeType = RangeStat;
                        break;
                    case EzRangeStat:
                        foundAttributeType = EzRangeStat;
                        break;
                }
                if (foundAttributeType != null) break; // Нашли нужный атрибут, выходим
            }

            if (foundAttributeType == null)
            {
                return null; // Нужный атрибут не найден
            }

            var name = structSymbol.Name;
            // Обработка случая, когда структура находится в глобальном пространстве имен
            var namespaceName = structSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty // Или null, в зависимости от того, что ожидает ваш генератор
                : structSymbol.ContainingNamespace.ToDisplayString();
            var accessibility = structSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(); // Используем InvariantCulture

            return new StructGenerationInfo(name, namespaceName, accessibility, foundAttributeType);
        }

        // Вызывает соответствующий генератор кода
        private static List<(string HintName, SourceText Source)> GenerateSourceCode(StructGenerationInfo info)
        {
            List<(string FileName, string SourceCode)> generatedItems;

            switch (info.AttributeType)
            {
                case Stat:
                    // Убедитесь, что StatGenerator.Generate статичен или создайте экземпляр
                    generatedItems = StatGenerator.Generate(info.Name, info.NamespaceName, info.Accessibility);
                    break;
                case RangeStat:
                    // Убедитесь, что RangeStatGenerator.Generate статичен или создайте экземпляр
                    generatedItems = RangeStatGenerator.Generate(info.Name, info.NamespaceName, info.Accessibility);
                    break;
                case EzRangeStat:
                    // Убедитесь, что EzRangeStatGenerator.Generate статичен или создайте экземпляр
                    generatedItems = EzRangeStatGenerator.Generate(info.Name, info.NamespaceName, info.Accessibility);
                    break;
                default:
                    // Не должно произойти, если логика GetStructGenerationInfo верна
                    return new List<(string HintName, SourceText Source)>();
            }

            // Преобразовать результат генераторов в нужный формат для RegisterSourceOutput
            var sourceTexts = new List<(string HintName, SourceText Source)>(generatedItems.Count);
            foreach (var (fileName, sourceCode) in generatedItems)
            {
                // Имя файла используется как "hint name"
                sourceTexts.Add((fileName, SourceText.From(sourceCode, Encoding.UTF8)));
            }
            return sourceTexts;
        }

        // Вспомогательная структура/запись для хранения информации о структуре
        private record StructGenerationInfo(string Name, string NamespaceName, string Accessibility, string AttributeType)
        {
            public string Name { get; } = Name;
            public string NamespaceName { get; } = NamespaceName;
            public string Accessibility { get; } = Accessibility;
            public string AttributeType { get; } = AttributeType;
        }
    }
}