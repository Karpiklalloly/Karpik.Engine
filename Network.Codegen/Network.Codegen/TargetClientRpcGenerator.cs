using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Network.Codegen;

[Generator]
public class TargetClientRpcGenerator : IIncrementalGenerator
{
    private const string TargetRpcCommandInterfaceName = "Network.ITargetRpcCommand";
    private const string ClientRpcCommandInterfaceName = "Network.IClientRpcCommand";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<Compilation> compilationProvider = context.CompilationProvider;
        context.RegisterSourceOutput(compilationProvider, Execute);
    }

    private void Execute(SourceProductionContext context, Compilation compilation)
    {
        // Определяем тип проекта
        var projectType = ProjectTypeDetector.DetectProjectType(compilation.AssemblyName ?? "");
        
        switch (projectType)
        {
            case ProjectTypeDetector.ProjectType.Server:
                GenerateServerCode(context, compilation);
                break;
            case ProjectTypeDetector.ProjectType.Client:
                GenerateClientCode(context, compilation);
                break;
            case ProjectTypeDetector.ProjectType.Shared:
                // Не генерируем RPC код в Shared проекте
                return;
            case ProjectTypeDetector.ProjectType.Unknown:
                // Выдаем предупреждение для неизвестных типов проектов
                var descriptor = new DiagnosticDescriptor(
                    "RPC001", 
                    "Unknown project type for RPC generation", 
                    "Cannot determine project type from assembly name '{0}'. RPC code will not be generated. Expected names to contain 'Server', 'Client', or 'Shared'.", 
                    "RPC", 
                    DiagnosticSeverity.Warning, 
                    true);
                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, compilation.AssemblyName));
                return;
        }
    }

    private void GenerateServerCode(SourceProductionContext context, Compilation compilation)
    {
        // Сбрасываем состояние CommandIdManager
        CommandIdManager.Reset();
        
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using LiteNetLib;");
        sb.AppendLine("using LiteNetLib.Utils;");
        sb.AppendLine("using Network;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ProjectTypeDetector.GetGeneratedNamespace(ProjectTypeDetector.ProjectType.Server)}");
        sb.AppendLine("{");
        sb.AppendLine("    public class TargetClientRpcSender");
        sb.AppendLine("    {");
        sb.AppendLine("        private static ThreadLocal<TargetClientRpcSender> _instance = new ThreadLocal<TargetClientRpcSender>(() => new TargetClientRpcSender());");
        sb.AppendLine("        public static TargetClientRpcSender Instance => _instance.Value;");
        sb.AppendLine("        private readonly NetDataWriter _writer = new NetDataWriter();");
        sb.AppendLine("        private NetManager _netManager;");
        sb.AppendLine();
        sb.AppendLine("        public void Initialize(NetManager netManager)");
        sb.AppendLine("        {");
        sb.AppendLine("            _netManager = netManager;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Найти все структуры, реализующие ITargetRpcCommand
        var targetRpcInfos = FindAllTargetRpcCommandInfos(compilation, context.CancellationToken);
        if (targetRpcInfos.Any())
        {
            sb.AppendLine("        // TargetRpc methods - send to specific client");
            sb.Append(GenerateTargetRpcSenderMethods(targetRpcInfos));
        }

        // Найти все структуры, реализующие IClientRpcCommand
        var clientRpcInfos = FindAllClientRpcCommandInfos(compilation, context.CancellationToken);
        if (clientRpcInfos.Any())
        {
            sb.AppendLine("        // ClientRpc methods - send to all clients or specific client");
            sb.Append(GenerateClientRpcSenderMethods(clientRpcInfos));
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        var fileName = ProjectTypeDetector.GetGeneratedFileName(ProjectTypeDetector.ProjectType.Server);
        context.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private void GenerateClientCode(SourceProductionContext context, Compilation compilation)
    {
        // Сбрасываем состояние CommandIdManager
        CommandIdManager.Reset();
        
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using LiteNetLib.Utils;");
        sb.AppendLine("using Karpik.Engine.Shared;");
        sb.AppendLine("using Karpik.Engine.Shared.DragonECS;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ProjectTypeDetector.GetGeneratedNamespace(ProjectTypeDetector.ProjectType.Client)}");
        sb.AppendLine("{");
        sb.AppendLine("    public class TargetClientRpcDispatcher");
        sb.AppendLine("    {");
        sb.AppendLine("        public static TargetClientRpcDispatcher Instance { get; } = new();");
        sb.AppendLine();
        
        sb.AppendLine("        private TargetClientRpcDispatcher() { }");
        sb.AppendLine();
        sb.AppendLine("        public void Dispatch(NetDataReader reader)");
        sb.AppendLine("        {");
        sb.AppendLine("            var commandId = reader.GetUShort();");
        sb.AppendLine("            switch (commandId)");
        sb.AppendLine("            {");

        // Найти все структуры, реализующие ITargetRpcCommand и IClientRpcCommand
        var targetRpcInfos = FindAllTargetRpcCommandInfos(compilation, context.CancellationToken);
        var clientRpcInfos = FindAllClientRpcCommandInfos(compilation, context.CancellationToken);

        if (targetRpcInfos.Any())
        {
            sb.Append(GenerateTargetRpcDispatcherCases(targetRpcInfos));
        }

        if (clientRpcInfos.Any())
        {
            sb.Append(GenerateClientRpcDispatcherCases(clientRpcInfos));
        }

        sb.AppendLine("                default:");
        sb.AppendLine("                    // Unknown command ID");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var fileName = ProjectTypeDetector.GetGeneratedFileName(ProjectTypeDetector.ProjectType.Client);
        context.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private List<CommandInfo> FindAllTargetRpcCommandInfos(Compilation compilation, CancellationToken ct)
    {
        var commandInterface = compilation.GetTypeByMetadataName(TargetRpcCommandInterfaceName);
        if (commandInterface == null)
        {
            return new List<CommandInfo>();
        }

        var commandInfos = new List<CommandInfo>();
        var allAssemblies = new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols);

        foreach (var assembly in allAssemblies)
        {
            ProcessNamespace(assembly.GlobalNamespace, commandInterface, commandInfos, ct);
        }

        // Сортируем и присваиваем детерминированные ID
        commandInfos = commandInfos.OrderBy(c => c.FullName).ToList();
        for (int i = 0; i < commandInfos.Count; i++)
        {
            var id = CommandIdManager.GetRpcCommandId(commandInfos[i].FullName, "TargetRpc");
            commandInfos[i] = commandInfos[i] with { Id = id };
        }

        return commandInfos;
    }

    private List<CommandInfo> FindAllClientRpcCommandInfos(Compilation compilation, CancellationToken ct)
    {
        var commandInterface = compilation.GetTypeByMetadataName(ClientRpcCommandInterfaceName);
        if (commandInterface == null)
        {
            return new List<CommandInfo>();
        }

        var commandInfos = new List<CommandInfo>();
        var allAssemblies = new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols);

        foreach (var assembly in allAssemblies)
        {
            ProcessNamespace(assembly.GlobalNamespace, commandInterface, commandInfos, ct);
        }

        // Сортируем и присваиваем детерминированные ID
        commandInfos = commandInfos.OrderBy(c => c.FullName).ToList();
        for (int i = 0; i < commandInfos.Count; i++)
        {
            var id = CommandIdManager.GetRpcCommandId(commandInfos[i].FullName, "ClientRpc");
            commandInfos[i] = commandInfos[i] with { Id = id };
        }

        return commandInfos;
    }

    private void ProcessNamespace(INamespaceSymbol namespaceSymbol, INamedTypeSymbol commandInterface, List<CommandInfo> commandInfos, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var typeMember in namespaceSymbol.GetTypeMembers())
        {
            if (typeMember.TypeKind == TypeKind.Struct && typeMember.AllInterfaces.Contains(commandInterface, SymbolEqualityComparer.Default))
            {
                var fields = typeMember.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(f => !f.IsStatic && f.CanBeReferencedByName)
                    .Select(f => new FieldInfo(f.Name, f.Type.ToDisplayString(), false))
                    .ToList();
                var properties = typeMember.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(f => !f.IsStatic && f.CanBeReferencedByName)
                    .Select(f => new FieldInfo(f.Name, f.Type.ToDisplayString(), true))
                    .ToList();
                fields = fields.Concat(properties).ToList();
                commandInfos.Add(new CommandInfo(0, typeMember.Name, typeMember.ToDisplayString(), fields));
            }
        }

        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            ProcessNamespace(nestedNamespace, commandInterface, commandInfos, ct);
        }
    }



    private static string GenerateTargetRpcSenderMethods(List<CommandInfo> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmd in commands)
        {
            var paramName = cmd.Name.Replace("TargetRpc", "").ToLower();
            sb.AppendLine($"        public void {cmd.Name.Replace("TargetRpc", "")}(NetPeer targetPeer, {cmd.FullName} {paramName}, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)");
            sb.AppendLine("        {");
            sb.AppendLine("            _writer.Reset();");
            sb.AppendLine("            _writer.Put((byte)PacketType.Command);");
            sb.AppendLine($"            _writer.Put((ushort){cmd.Id}); // {cmd.FullName}");
            foreach (var field in cmd.Fields)
            {
                sb.AppendLine($"            _writer.Put({paramName}.{field.Name});");
            }
            sb.AppendLine("            targetPeer.Send(_writer, deliveryMethod);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    private static string GenerateClientRpcSenderMethods(List<CommandInfo> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmd in commands)
        {
            var paramName = cmd.Name.Replace("ClientRpc", "").ToLower();
            
            // Method to send to specific client
            sb.AppendLine($"        public void {cmd.Name.Replace("ClientRpc", "")}(NetPeer targetPeer, {cmd.FullName} {paramName}, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)");
            sb.AppendLine("        {");
            sb.AppendLine("            _writer.Reset();");
            sb.AppendLine("            _writer.Put((byte)PacketType.Command);");
            sb.AppendLine($"            _writer.Put((ushort){cmd.Id}); // {cmd.FullName}");
            foreach (var field in cmd.Fields)
            {
                sb.AppendLine($"            _writer.Put({paramName}.{field.Name});");
            }
            sb.AppendLine("            targetPeer.Send(_writer, deliveryMethod);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Method to send to all clients
            sb.AppendLine($"        public void {cmd.Name.Replace("ClientRpc", "")}All({cmd.FullName} {paramName}, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)");
            sb.AppendLine("        {");
            sb.AppendLine("            _writer.Reset();");
            sb.AppendLine("            _writer.Put((byte)PacketType.Command);");
            sb.AppendLine($"            _writer.Put((ushort){cmd.Id}); // {cmd.FullName}");
            foreach (var field in cmd.Fields)
            {
                sb.AppendLine($"            _writer.Put({paramName}.{field.Name});");
            }
            sb.AppendLine("            _netManager.SendToAll(_writer, deliveryMethod);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    private static string GenerateDispatcherStart()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using Karpik.Engine.Shared;");
        sb.AppendLine("using LiteNetLib.Utils;");
        sb.AppendLine("using Network;");
        sb.AppendLine("using Karpik.Engine.Shared.DragonECS;");
        sb.AppendLine();
        sb.AppendLine("namespace Karpik.Engine.Client");
        sb.AppendLine("{");
        
        sb.AppendLine($"    public partial class TargetClientRpcDispatcher");
        sb.AppendLine("    {");
        
        sb.AppendLine("        public static TargetClientRpcDispatcher Instance { get; } = new TargetClientRpcDispatcher();");
        sb.AppendLine("        public void Dispatch(NetDataReader reader)");
        sb.AppendLine("        {");
        sb.AppendLine("            var commandId = reader.GetUShort();");
        sb.AppendLine("            switch (commandId)");
        sb.AppendLine("            {");
        return sb.ToString();
    }
    
    private static string GenerateDispatcherEnd()
    {
        var sb = new StringBuilder();
        sb.AppendLine("                default:");
        sb.AppendLine("                    // Unknown command ID");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateTargetRpcDispatcherCases(List<CommandInfo> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmdInfo in commands)
        {
            sb.AppendLine($"                case {cmdInfo.Id}: // {cmdInfo.FullName}");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var cmd = new {cmdInfo.FullName}();");
            foreach (var field in cmdInfo.Fields)
            {
                sb.AppendLine($"                    cmd.{field.Name} = reader.Get{GetReaderMethod(field.TypeName)}();");
            }
            sb.AppendLine($"                    Worlds.Instance.EventWorld.SendEvent(cmd);");
            sb.AppendLine("                    break;");
            sb.AppendLine("                }");
        }
 
        return sb.ToString();
    }

    private static string GenerateClientRpcDispatcherCases(List<CommandInfo> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmdInfo in commands)
        {
            sb.AppendLine($"                case {cmdInfo.Id}: // {cmdInfo.FullName}");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var cmd = new {cmdInfo.FullName}();");
            foreach (var field in cmdInfo.Fields)
            {
                sb.AppendLine($"                    cmd.{field.Name} = reader.Get{GetReaderMethod(field.TypeName)}();");
            }
            sb.AppendLine($"                    Worlds.Instance.EventWorld.SendEvent(cmd);");
            sb.AppendLine("                    break;");
            sb.AppendLine("                }");
        }
 
        return sb.ToString();
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

// Используем те же структуры данных что и в основном RpcGenerator
internal record FieldInfo(string Name, string TypeName, bool IsProperty)
{
    public string Name { get; } = Name;
    public string TypeName { get; } = TypeName;
    public bool IsProperty { get; } = IsProperty;
}

internal record CommandInfo(uint Id, string Name, string FullName, List<FieldInfo> Fields)
{
    public uint Id { get; set; } = Id;
    public string Name { get; } = Name;
    public string FullName { get; } = FullName;
    public List<FieldInfo> Fields { get; } = Fields;
}