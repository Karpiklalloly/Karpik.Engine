using System;
using System.Collections.Generic;

namespace Network.Codegen;

internal static class CommandIdManager
{
    private static readonly Dictionary<string, uint> _assignedIds = new();
    private static uint _nextId = 1;

    public static uint GetOrAssignId(string fullName)
    {
        if (_assignedIds.TryGetValue(fullName, out var existingId))
        {
            return existingId;
        }

        var newId = _nextId++;
        _assignedIds[fullName] = newId;
        return newId;
    }

    public static uint GetDeterministicId(string fullName)
    {
        if (_assignedIds.TryGetValue(fullName, out var existingId))
        {
            return existingId;
        }

        // Генерируем детерминированный ID на основе хеша имени
        var hash = fullName.GetHashCode();
        // Используем абсолютное значение и ограничиваем диапазон
        // Избегаем 0 и слишком больших чисел
        var id = (uint)(Math.Abs(hash) % 65534) + 1;
        
        // Проверяем на коллизии и разрешаем их
        while (_assignedIds.ContainsValue(id))
        {
            id = (id % 65534) + 1;
        }

        _assignedIds[fullName] = id;
        return id;
    }

    public static uint GetRpcCommandId(string commandFullName, string rpcType)
    {
        var key = $"{rpcType}.{commandFullName}";
        return GetDeterministicId(key);
    }

    public static void Reset()
    {
        _assignedIds.Clear();
        _nextId = 1;
    }

    public static Dictionary<string, uint> GetAllAssignedIds()
    {
        return new Dictionary<string, uint>(_assignedIds);
    }
}