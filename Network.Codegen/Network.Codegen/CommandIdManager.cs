using System;
using System.Collections.Generic;
using System.Linq;

namespace Network.Codegen;

internal static class CommandIdManager
{
    /// <summary>
    /// Получает детерминированный ID для команды. Все типы команд используют единую систему нумерации.
    /// </summary>
    /// <param name="fullName">Полное имя команды</param>
    /// <returns>Детерминированный ID команды</returns>
    public static uint GetOrAssignId(string fullName)
    {
        return GetDeterministicCommandId(fullName);
    }

    /// <summary>
    /// Получает детерминированный ID для команды на основе хеша имени
    /// </summary>
    /// <param name="fullName">Полное имя команды</param>
    /// <returns>Детерминированный ID команды</returns>
    private static uint GetDeterministicCommandId(string fullName)
    {
        // Используем стабильный хеш-алгоритм для детерминированного ID
        var hash = ComputeStableHash(fullName);
        
        // Ограничиваем диапазон ID (1-65535) и избегаем 0
        var id = (uint)((hash % 65534) + 1);
        
        return id;
    }
    
    /// <summary>
    /// Вычисляет стабильный хеш строки (не зависит от версии .NET)
    /// </summary>
    /// <param name="input">Входная строка</param>
    /// <returns>Стабильный хеш</returns>
    private static int ComputeStableHash(string input)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char c in input)
            {
                hash = ((hash << 5) + hash) + c;
            }
            return Math.Abs(hash);
        }
    }

    /// <summary>
    /// Получает ID для RPC команды используя общий счетчик
    /// </summary>
    /// <param name="commandFullName">Полное имя команды</param>
    /// <param name="rpcType">Тип RPC (TargetRpc, ClientRpc)</param>
    /// <returns>Уникальный ID команды</returns>
    public static uint GetRpcCommandId(string commandFullName, string rpcType)
    {
        // Используем общий счетчик для всех типов команд
        var key = $"{rpcType}.{commandFullName}";
        return GetOrAssignId(key);
    }

    /// <summary>
    /// Сбрасывает состояние менеджера ID (для совместимости, детерминированные ID не требуют сброса)
    /// </summary>
    public static void Reset()
    {
        // Детерминированные ID не требуют сброса состояния
    }

    /// <summary>
    /// Получает все назначенные ID для отладки (симулирует для детерминированных ID)
    /// </summary>
    public static Dictionary<string, uint> GetAllAssignedIds()
    {
        // Для детерминированных ID возвращаем пустой словарь
        // Реальные ID будут показаны в отладочном файле
        return new Dictionary<string, uint>();
    }

    /// <summary>
    /// Получает следующий доступный ID (не применимо для детерминированных ID)
    /// </summary>
    public static uint GetNextAvailableId()
    {
        // Для детерминированных ID концепция "следующего ID" не применима
        return 0;
    }
    
    /// <summary>
    /// Проверяет потенциальные коллизии ID для списка команд
    /// </summary>
    /// <param name="commandNames">Список имен команд</param>
    /// <returns>Словарь с коллизиями (ID -> список команд)</returns>
    public static Dictionary<uint, List<string>> CheckCollisions(IEnumerable<string> commandNames)
    {
        var collisions = new Dictionary<uint, List<string>>();
        
        foreach (var commandName in commandNames)
        {
            var id = GetOrAssignId(commandName);
            
            if (!collisions.ContainsKey(id))
            {
                collisions[id] = new List<string>();
            }
            
            collisions[id].Add(commandName);
        }
        
        // Возвращаем только реальные коллизии (где больше одной команды)
        return collisions.Where(kvp => kvp.Value.Count > 1)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}