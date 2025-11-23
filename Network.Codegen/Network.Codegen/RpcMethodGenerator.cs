using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Network.Codegen;

[Generator]
public class LegacyRpcMethodGenerator : IIncrementalGenerator
{
    private const string TargetRpcAttributeName = "Network.TargetRpcAttribute";
    private const string ClientRpcAttributeName = "Network.ClientRpcAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methodProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetMethodForGeneration(ctx))
            .Where(static m => m is not null);

        var compilation = context.CompilationProvider.Combine(methodProvider.Collect());

        context.RegisterSourceOutput(compilation, static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static MethodInfo? GetMethodForGeneration(GeneratorSyntaxContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        foreach (var attributeList in methodDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol attributeSymbol)
                {
                    var attributeType = attributeSymbol.ContainingType.ToDisplayString();
                    if (attributeType == TargetRpcAttributeName || attributeType == ClientRpcAttributeName)
                    {
                        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                        if (methodSymbol != null)
                        {
                            // Проверяем, что метод принимает ровно один параметр - ECS компонент-событие
                            if (methodSymbol.Parameters.Length != 1)
                            {
                                continue; // Пропускаем методы с неправильным количеством параметров
                            }

                            var parameter = methodSymbol.Parameters[0];
                            var parameterType = parameter.Type;

                            // Проверяем, что параметр реализует IEcsComponentEvent
                            var isEcsEvent = parameterType.AllInterfaces.Any(i =>
                                i.ToDisplayString() == "DCFApixels.DragonECS.IEcsComponentEvent");

                            if (!isEcsEvent)
                            {
                                continue; // Пропускаем методы, которые не принимают ECS события
                            }

                            // Получаем поля компонента для сериализации
                            var fields = GetEventFields(parameterType);

                            var rpcType = attributeType == TargetRpcAttributeName ? RpcType.Target : RpcType.Client;
                            var className = methodSymbol.ContainingType.Name;
                            var namespaceName = methodSymbol.ContainingNamespace.ToDisplayString();

                            return new MethodInfo(
                                methodSymbol.Name,
                                className,
                                namespaceName,
                                parameterType.ToDisplayString(),
                                fields,
                                rpcType);
                        }
                    }
                }
            }
        }
        return null;
    }

    private static List<FieldInfo> GetEventFields(ITypeSymbol eventType)
    {
        var fields = new List<FieldInfo>();

        // Получаем все поля структуры
        foreach (var member in eventType.GetMembers())
        {
            if (member is IFieldSymbol field && !field.IsStatic && field.CanBeReferencedByName)
            {
                fields.Add(new FieldInfo(field.Name, field.Type.ToDisplayString(), false));
            }
            else if (member is IPropertySymbol property && !property.IsStatic && property.CanBeReferencedByName)
            {
                fields.Add(new FieldInfo(property.Name, property.Type.ToDisplayString(), true));
            }
        }

        return fields;
    }

    private static void Execute(Compilation compilation, ImmutableArray<MethodInfo?> methods, SourceProductionContext context)
    {
        var methodList = methods.ToList();
        if (!methodList.Any()) return;

        var targetRpcMethods = methodList.Where(m => m.Value.RpcType == RpcType.Target).ToList();
        var clientRpcMethods = methodList.Where(m => m.Value.RpcType == RpcType.Client).ToList();

        if (targetRpcMethods.Any())
        {
            GenerateTargetRpcCode(targetRpcMethods, context);
        }

        if (clientRpcMethods.Any())
        {
            GenerateClientRpcCode(clientRpcMethods, context);
        }
    }

    private static void GenerateTargetRpcCode(List<MethodInfo?> methods, SourceProductionContext context)
    {
        var methodIds = new Dictionary<string, uint>();

        // Генерируем серверную часть для отправки TargetRpc
        var serverSb = new StringBuilder();
        serverSb.AppendLine("// <auto-generated/>");
        serverSb.AppendLine("using LiteNetLib;");
        serverSb.AppendLine("using LiteNetLib.Utils;");
        serverSb.AppendLine("using Network;");
        serverSb.AppendLine("using DCFApixels.DragonECS;");
        serverSb.AppendLine();
        serverSb.AppendLine("namespace Karpik.Engine.Server.Generated");
        serverSb.AppendLine("{");
        serverSb.AppendLine("    public static class TargetRpcSender");
        serverSb.AppendLine("    {");
        serverSb.AppendLine("        private static readonly NetDataWriter _writer = new NetDataWriter();");
        serverSb.AppendLine();

        foreach (var method in methods.Select(x => x.Value))
        {
            var methodKey = $"TargetRpc.{method.ClassName}.{method.MethodName}";
            var methodId = CommandIdManager.GetOrAssignId(methodKey);
            methodIds[methodKey] = methodId;

            serverSb.AppendLine($"        public static void {method.MethodName}(NetPeer targetPeer, {method.EventTypeName} eventData)");
            serverSb.AppendLine("        {");
            serverSb.AppendLine("            _writer.Reset();");
            serverSb.AppendLine("            _writer.Put((byte)PacketType.Command);");
            serverSb.AppendLine($"            _writer.Put((ushort){methodId});");

            // Прямая сериализация полей
            foreach (var field in method.Fields)
            {
                serverSb.AppendLine($"            _writer.Put(eventData.{field.Name});");
            }

            serverSb.AppendLine("            targetPeer.Send(_writer, DeliveryMethod.ReliableOrdered);");
            serverSb.AppendLine("        }");
            serverSb.AppendLine();
        }

        serverSb.AppendLine("    }");
        serverSb.AppendLine("}");

        // Генерируем клиентскую часть для обработки TargetRpc
        var clientSb = new StringBuilder();
        clientSb.AppendLine("// <auto-generated/>");
        clientSb.AppendLine("using LiteNetLib.Utils;");
        clientSb.AppendLine("using Karpik.Engine.Shared;");
        clientSb.AppendLine("using DCFApixels.DragonECS;");
        clientSb.AppendLine();
        clientSb.AppendLine("namespace Karpik.Engine.Client");
        clientSb.AppendLine("{");
        clientSb.AppendLine("    public class TargetRpcDispatcher");
        clientSb.AppendLine("    {");
        clientSb.AppendLine("        public void Dispatch(NetDataReader reader)");
        clientSb.AppendLine("        {");
        clientSb.AppendLine("            var methodId = reader.GetUShort();");
        clientSb.AppendLine("            switch (methodId)");
        clientSb.AppendLine("            {");

        foreach (var method in methods.Select(x => x.Value))
        {
            var methodKey = $"TargetRpc.{method.ClassName}.{method.MethodName}";
            var currentMethodId = methodIds[methodKey];

            clientSb.AppendLine($"                case {currentMethodId}: // {method.ClassName}.{method.MethodName}");
            clientSb.AppendLine("                {");
            clientSb.AppendLine($"                    var eventData = new {method.EventTypeName}();");

            // Прямая десериализация полей
            foreach (var field in method.Fields)
            {
                clientSb.AppendLine($"                    eventData.{field.Name} = reader.Get{GetReaderMethod(field.TypeName)}();");
            }

            clientSb.AppendLine("                    Worlds.Instance.EventWorld.SendEvent(eventData);");
            clientSb.AppendLine("                    break;");
            clientSb.AppendLine("                }");
        }

        clientSb.AppendLine("                default:");
        clientSb.AppendLine("                    break;");
        clientSb.AppendLine("            }");
        clientSb.AppendLine("        }");
        clientSb.AppendLine("    }");
        clientSb.AppendLine("}");

        context.AddSource("TargetRpcServer.g.cs", SourceText.From(serverSb.ToString(), Encoding.UTF8));
        context.AddSource("TargetRpcClient.g.cs", SourceText.From(clientSb.ToString(), Encoding.UTF8));
    }

    private static void GenerateClientRpcCode(List<MethodInfo?> methods, SourceProductionContext context)
    {
        var methodIds = new Dictionary<string, uint>();

        // Генерируем серверную часть для отправки ClientRpc
        var serverSb = new StringBuilder();
        serverSb.AppendLine("// <auto-generated/>");
        serverSb.AppendLine("using LiteNetLib;");
        serverSb.AppendLine("using LiteNetLib.Utils;");
        serverSb.AppendLine("using Network;");
        serverSb.AppendLine("using DCFApixels.DragonECS;");
        serverSb.AppendLine();
        serverSb.AppendLine("namespace Karpik.Engine.Server.Generated");
        serverSb.AppendLine("{");
        serverSb.AppendLine("    public static class ClientRpcSender");
        serverSb.AppendLine("    {");
        serverSb.AppendLine("        private static readonly NetDataWriter _writer = new NetDataWriter();");
        serverSb.AppendLine();

        foreach (var method in methods.Select(x => x.Value))
        {
            var methodKey = $"ClientRpc.{method.ClassName}.{method.MethodName}";
            var methodId = CommandIdManager.GetOrAssignId(methodKey);
            methodIds[methodKey] = methodId;

            // Метод для отправки одному клиенту
            serverSb.AppendLine($"        public static void {method.MethodName}(NetPeer targetPeer, {method.EventTypeName} eventData)");
            serverSb.AppendLine("        {");
            serverSb.AppendLine("            _writer.Reset();");
            serverSb.AppendLine("            _writer.Put((byte)PacketType.Command);");
            serverSb.AppendLine($"            _writer.Put((ushort){methodId});");

            // Прямая сериализация полей
            foreach (var field in method.Fields)
            {
                serverSb.AppendLine($"            _writer.Put(eventData.{field.Name});");
            }

            serverSb.AppendLine("            targetPeer.Send(_writer, DeliveryMethod.ReliableOrdered);");
            serverSb.AppendLine("        }");
            serverSb.AppendLine();

            // Метод для отправки всем клиентам
            serverSb.AppendLine($"        public static void {method.MethodName}All(NetManager netManager, {method.EventTypeName} eventData)");
            serverSb.AppendLine("        {");
            serverSb.AppendLine("            _writer.Reset();");
            serverSb.AppendLine("            _writer.Put((byte)PacketType.Command);");
            serverSb.AppendLine($"            _writer.Put((ushort){methodId});");

            // Прямая сериализация полей
            foreach (var field in method.Fields)
            {
                serverSb.AppendLine($"            _writer.Put(eventData.{field.Name});");
            }

            serverSb.AppendLine("            netManager.SendToAll(_writer, DeliveryMethod.ReliableOrdered);");
            serverSb.AppendLine("        }");
            serverSb.AppendLine();
        }

        serverSb.AppendLine("    }");
        serverSb.AppendLine("}");

        // Генерируем клиентскую часть для обработки ClientRpc
        var clientSb = new StringBuilder();
        clientSb.AppendLine("// <auto-generated/>");
        clientSb.AppendLine("using LiteNetLib.Utils;");
        clientSb.AppendLine("using Karpik.Engine.Shared;");
        clientSb.AppendLine("using DCFApixels.DragonECS;");
        clientSb.AppendLine();
        clientSb.AppendLine("namespace Karpik.Engine.Client");
        clientSb.AppendLine("{");
        clientSb.AppendLine("    public class ClientRpcDispatcher");
        clientSb.AppendLine("    {");
        clientSb.AppendLine("        public void Dispatch(NetDataReader reader)");
        clientSb.AppendLine("        {");
        clientSb.AppendLine("            var methodId = reader.GetUShort();");
        clientSb.AppendLine("            switch (methodId)");
        clientSb.AppendLine("            {");

        foreach (var method in methods.Select(x => x.Value))
        {
            var methodKey = $"ClientRpc.{method.ClassName}.{method.MethodName}";
            var currentMethodId = methodIds[methodKey];

            clientSb.AppendLine($"                case {currentMethodId}: // {method.ClassName}.{method.MethodName}");
            clientSb.AppendLine("                {");
            clientSb.AppendLine($"                    var eventData = new {method.EventTypeName}();");

            // Прямая десериализация полей
            foreach (var field in method.Fields)
            {
                clientSb.AppendLine($"                    eventData.{field.Name} = reader.Get{GetReaderMethod(field.TypeName)}();");
            }

            clientSb.AppendLine("                    Worlds.Instance.EventWorld.SendEvent(eventData);");
            clientSb.AppendLine("                    break;");
            clientSb.AppendLine("                }");
        }

        clientSb.AppendLine("                default:");
        clientSb.AppendLine("                    break;");
        clientSb.AppendLine("            }");
        clientSb.AppendLine("        }");
        clientSb.AppendLine("    }");
        clientSb.AppendLine("}");

        context.AddSource("ClientRpcServer.g.cs", SourceText.From(serverSb.ToString(), Encoding.UTF8));
        context.AddSource("ClientRpcClient.g.cs", SourceText.From(clientSb.ToString(), Encoding.UTF8));
    }

    private static string GetReaderMethod(string typeName)
    {
        return typeName switch
        {
            "float" or "System.Single" => "Float",
            "int" or "System.Int32" => "Int",
            "bool" or "System.Boolean" => "Bool",
            "uint" or "System.UInt32" => "UInt",
            "ushort" or "System.UInt16" => "UShort",
            "byte" or "System.Byte" => "Byte",
            "string" or "System.String" => "String",
            "double" or "System.Double" => "Double",
            _ => typeName.Split('.').Last()
        };
    }
}

internal readonly record struct MethodInfo(
    string MethodName,
    string ClassName,
    string NamespaceName,
    string EventTypeName,
    List<FieldInfo> Fields,
    RpcType RpcType)
{
    public string MethodName { get; } = MethodName;
    public string ClassName { get; } = ClassName;
    public string NamespaceName { get; } = NamespaceName;
    public string EventTypeName { get; } = EventTypeName;
    public List<FieldInfo> Fields { get; } = Fields;
    public RpcType RpcType { get; } = RpcType;
}

internal enum RpcType
{
    Target,
    Client
}