using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Karpik.Engine.Core.Hot
{
    public class TypeMapper
    {
        // Кэш для прямого маппинга типов (oldType -> newType)
        private readonly Dictionary<Type, Type> _directTypeMap;
        // Для быстрого поиска новых типов по их полному имени
        private readonly Dictionary<string, Type> _newTypesByFullName;

        public TypeMapper(IEnumerable<Assembly> oldAssemblies, IEnumerable<Assembly> newAssemblies)
        {
            _newTypesByFullName = newAssemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.FullName != null)
                .DistinctBy(t => t.FullName!) // Используем FullName для начального прямого маппинга
                .ToDictionary(t => t.FullName!);

            _directTypeMap = new Dictionary<Type, Type>();
            var oldTypes = oldAssemblies.SelectMany(a => a.GetTypes());
            
            // Заполняем _directTypeMap для всех не-generic типов и generic-определений
            // и для полностью сконструированных generic-типов, если их FullName совпадает
            foreach (var oldType in oldTypes)
            {
                if (oldType.FullName != null && _newTypesByFullName.TryGetValue(oldType.FullName, out var newType))
                {
                    _directTypeMap[oldType] = newType;
                }
            }
        }

        public Type? GetNewType(Type oldType)
        {
            // 1. Проверяем кэш для уже обработанных типов
            if (_directTypeMap.TryGetValue(oldType, out var newType))
            {
                return newType;
            }

            // 2. Обрабатываем generic-типы, рекурсивно перемаппируя их аргументы
            if (oldType.IsGenericType && !oldType.IsGenericTypeDefinition)
            {
                Type genericTypeDefinition = oldType.GetGenericTypeDefinition();
                Type[] oldGenericArguments = oldType.GetGenericArguments();

                // Ремаппим само generic-определение (например, List<> или Dictionary<,>)
                Type? newGenericTypeDefinition = GetNewType(genericTypeDefinition);
                if (newGenericTypeDefinition == null)
                {
                    // Если определение не перемаппировано, используем оригинальное
                    newGenericTypeDefinition = genericTypeDefinition;
                }

                // Ремаппим каждый generic-аргумент
                Type[] newGenericArguments = new Type[oldGenericArguments.Length];
                bool argumentsChanged = false;
                for (int i = 0; i < oldGenericArguments.Length; i++)
                {
                    Type? remappedArg = GetNewType(oldGenericArguments[i]);
                    if (remappedArg != null && remappedArg != oldGenericArguments[i])
                    {
                        newGenericArguments[i] = remappedArg;
                        argumentsChanged = true;
                    }
                    else
                    {
                        newGenericArguments[i] = oldGenericArguments[i];
                    }
                }

                // Если изменилось generic-определение или хотя бы один аргумент, конструируем новый generic-тип
                if (newGenericTypeDefinition != genericTypeDefinition || argumentsChanged)
                {
                    try
                    {
                        Type constructedNewType = newGenericTypeDefinition.MakeGenericType(newGenericArguments);
                        // Кэшируем результат для будущих запросов
                        _directTypeMap[oldType] = constructedNewType;
                        return constructedNewType;
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"[TypeMapper] Failed to construct generic type for {oldType.FullName}: {ex.Message}");
                        // Возвращаем null, если конструирование не удалось
                        return null;
                    }
                }
            }

            // 3. Если тип не найден в прямом маппинге и не является generic-типом, который можно перемаппировать,
            // пытаемся найти его по FullName в новых сборках (для типов, которые могли быть добавлены/изменены,
            // но не были generic-аргументами или определениями).
            // Это может быть избыточно, если _directTypeMap уже содержит все прямые совпадения.
            // Но для надежности оставим.
            if (oldType.FullName != null && _newTypesByFullName.TryGetValue(oldType.FullName, out newType))
            {
                _directTypeMap[oldType] = newType; // Кэшируем
                return newType;
            }

            // 4. Если ничего не найдено, возвращаем null
            return null;
        }
    }
}