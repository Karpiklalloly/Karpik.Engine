using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Network.Codegen;

/// <summary>
/// Глобальный координатор для управления ID команд между всеми генераторами
/// </summary>
internal static class GlobalCommandIdCoordinator
{
    private static readonly object _lock = new object();
    private static readonly Dictionary<string, uint> _globalCommandIds = new();
    private static uint _nextGlobalId = 1;
    private static bool _isInitialized = false;

    /// <summary>
    /// Инициализирует координатор (вызывается один раз за компиляцию)
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            if (!_isInitialized)
            {
                _globalCommandIds.Clear();
                _nextGlobalId = 1;
                _isInitialized = true;
            }
        }
    }

    /// <summary>
    /// Получает или назначает глобальный ID для команды
    /// </summary>
    /// <param name="commandKey">Ключ команды (например, "StateCommand.MoveCommand" или "TargetRpc.ShowMessage")</param>
    /// <returns>Уникальный глобальный ID</returns>
    public static uint GetOrAssignGlobalId(string commandKey)
    {
        lock (_lock)
        {
            if (_globalCommandIds.TryGetValue(commandKey, out var existingId))
            {
                return existingId;
            }

            var newId = _nextGlobalId++;
            _globalCommandIds[commandKey] = newId;
            return newId;
        }
    }

    /// <summary>
    /// Получает все назначенные ID для отладки
    /// </summary>
    public static Dictionary<string, uint> GetAllAssignedIds()
    {
        lock (_lock)
        {
            return new Dictionary<string, uint>(_globalCommandIds);
        }
    }

    /// <summary>
    /// Сбрасывает состояние координатора (для новой компиляции)
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _globalCommandIds.Clear();
            _nextGlobalId = 1;
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Получает следующий доступный ID без назначения
    /// </summary>
    public static uint GetNextAvailableId()
    {
        lock (_lock)
        {
            return _nextGlobalId;
        }
    }

    /// <summary>
    /// Проверяет, инициализирован ли координатор
    /// </summary>
    public static bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return _isInitialized;
            }
        }
    }
}