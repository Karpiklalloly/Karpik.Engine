using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Karpik.Engine.Shared.AssetManagement.Core;

public class LooseAssemblyNameBinder : ISerializationBinder
{
    private readonly List<Assembly> _assemblies = [];

    public LooseAssemblyNameBinder()
    {
        // Кэшируем сборки текущего домена, чтобы искать в них типы
        var allAssemblies = AppDomain.CurrentDomain.GetAssemblies().GroupBy(x => x.FullName).ToList();
        foreach (var allAssembly in allAssemblies)
        {
            _assemblies.Add(allAssembly.Last());
        }
    }
    
    public Type BindToType(string? assemblyName, string typeName)
    {
        // 1. Сначала пробуем стандартный механизм (если типы совпадают идеально)
        Type? typeToDeserialize = null;
        try 
        {
            // Попытка получить тип по полному имени
            string resolvedTypeName = $"{typeName}, {assemblyName}";
            typeToDeserialize = Type.GetType(resolvedTypeName);
        }
        catch { }

        if (typeToDeserialize != null) 
            return typeToDeserialize;

        // 2. Если не вышло, ищем тип среди загруженных сборок, игнорируя версию сборки
        // Примечание: Это предполагает, что Namespace и имя класса не менялись
        foreach (var assembly in _assemblies)
        {
            typeToDeserialize = assembly.GetTypes().FirstOrDefault(x => x.FullName.Contains(typeName));
            if (typeToDeserialize != null)
            {
                return typeToDeserialize;
            }
        }

        throw new JsonSerializationException($"Could not resolve type '{typeName}' from assembly '{assemblyName}'.");
    }

    public void BindToName(Type serializedType, [UnscopedRef] out string? assemblyName, [UnscopedRef] out string? typeName)
    {
        assemblyName = serializedType.Assembly.GetName().Name; 
        typeName = serializedType.FullName;
    }
}