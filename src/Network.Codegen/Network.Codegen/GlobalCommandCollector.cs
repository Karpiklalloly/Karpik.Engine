using System.Collections.Generic;
using System.Linq;

namespace Network.Codegen;

/// <summary>
/// Глобальный коллектор для сбора всех команд из разных генераторов
/// </summary>
internal static class GlobalCommandCollector
{
    private static readonly object _lock = new object();
    private static readonly HashSet<string> _allCommands = new HashSet<string>();
    private static bool _isFinalized = false;

    /// <summary>
    /// Добавляет команды в глобальную коллекцию
    /// </summary>
    /// <param name="commands">Список команд для добавления</param>
    public static void AddCommands(IEnumerable<string> commands)
    {
        lock (_lock)
        {
            if (_isFinalized)
            {
                return; // Не добавляем команды после финализации
            }

            foreach (var command in commands)
            {
                _allCommands.Add(command);
            }
        }
    }

    /// <summary>
    /// Получает все собранные команды и финализирует коллекцию
    /// </summary>
    /// <returns>Список всех команд</returns>
    public static List<string> GetAllCommandsAndFinalize()
    {
        lock (_lock)
        {
            _isFinalized = true;
            return _allCommands.ToList();
        }
    }

    /// <summary>
    /// Сбрасывает состояние коллектора (для новой компиляции)
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _allCommands.Clear();
            _isFinalized = false;
        }
    }

    /// <summary>
    /// Проверяет, финализирован ли коллектор
    /// </summary>
    public static bool IsFinalized
    {
        get
        {
            lock (_lock)
            {
                return _isFinalized;
            }
        }
    }

    /// <summary>
    /// Получает количество собранных команд
    /// </summary>
    public static int Count
    {
        get
        {
            lock (_lock)
            {
                return _allCommands.Count;
            }
        }
    }
}