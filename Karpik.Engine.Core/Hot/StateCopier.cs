using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Karpik.Engine.Core.Hot
{
    public static class StateCopier
    {
        private const BindingFlags FieldBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Изменяем сигнатуру, чтобы принимать processedObjects
        public static void CopyState(object source, object destination, TypeMapper typeMapper, Dictionary<object, object> processedObjects)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (typeMapper == null) throw new ArgumentNullException(nameof(typeMapper));
            if (processedObjects == null) throw new ArgumentNullException(nameof(processedObjects)); // Добавлена проверка

            // Если destination уже был обработан как часть цикла, возвращаемся, чтобы избежать бесконечной рекурсии
            // Этот check здесь, чтобы обработать случаи, когда destination является частью цикла,
            // но не был sourceObject в ConvertObject
            if (processedObjects.ContainsValue(destination))
            {
                return;
            }

            var destinationType = destination.GetType();
            var sourceFields = source.GetType().GetFields(FieldBindingFlags);
            var destinationFields = destinationType.GetFields(FieldBindingFlags).ToDictionary(f => f.Name);

            foreach (var sourceField in sourceFields)
            {
                if (destinationFields.TryGetValue(sourceField.Name, out var destinationField))
                {
                    try
                    {
                        var sourceValue = sourceField.GetValue(source);
                        // Передаем processedObjects дальше
                        var convertedValue = ConvertObject(sourceValue, typeMapper, processedObjects);
                        destinationField.SetValue(destination, convertedValue);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StateCopier] An unexpected error occurred while copying field '{sourceField.Name}': {ex.Message}");
                    }
                }
            }
        }

        private static object? ConvertObject(object? sourceObject, TypeMapper typeMapper, Dictionary<object, object> processedObjects)
        {
            if (sourceObject == null) return null;
            
            // Проверяем циклические ссылки в самом начале
            if (processedObjects.TryGetValue(sourceObject, out var existing)) return existing;

            var sourceObjectType = sourceObject.GetType();

            // Primitives, enums, strings can be returned directly.
            if (sourceObjectType.IsPrimitive || sourceObjectType.IsEnum || sourceObjectType == typeof(string))
            {
                return sourceObject;
            }

            var remappedType = typeMapper.GetNewType(sourceObjectType);
            
            if (remappedType == null)
            {
                string assemblyName = sourceObjectType.Assembly.GetName().Name;
                if (assemblyName.StartsWith("System") || assemblyName == "mscorlib" || assemblyName == "netstandard")
                {
                    return sourceObject;
                }
                else
                {
                    Console.WriteLine($"[StateCopier] Could not remap type '{sourceObjectType.FullName}' from assembly '{assemblyName}'. It might be missing from the hot-reload scope. State will be lost.");
                    return null;
                }
            }

            // Handle collections
            if (typeof(IEnumerable).IsAssignableFrom(remappedType) && remappedType != typeof(string))
            {
                object? newCollection = null;
                if (!remappedType.IsAbstract && !remappedType.IsInterface && remappedType.GetConstructor(Type.EmptyTypes) != null)
                {
                    newCollection = Activator.CreateInstance(remappedType);
                }

                if (newCollection != null)
                {
                    processedObjects[sourceObject] = newCollection; // Добавляем в кэш перед итерацией для обработки самоссылающихся коллекций

                    if (sourceObject is IDictionary oldDict && newCollection is IDictionary newDict)
                    {
                        foreach (DictionaryEntry entry in oldDict)
                        {
                            var newKey = ConvertObject(entry.Key, typeMapper, processedObjects);
                            var newValue = ConvertObject(entry.Value, typeMapper, processedObjects);
                            if (newKey != null) newDict.Add(newKey, newValue);
                        }
                    }
                    else if (sourceObject is IList oldList && newCollection is IList newList)
                    {
                        foreach (var item in oldList)
                        {
                            newList.Add(ConvertObject(item, typeMapper, processedObjects));
                        }
                    }
                }
                return newCollection;
            }

            // Handle other reference types
            if (!remappedType.IsAbstract && !remappedType.IsInterface && remappedType.GetConstructor(Type.EmptyTypes) != null)
            {
                var newInstance = Activator.CreateInstance(remappedType);
                if (newInstance != null)
                {
                    processedObjects[sourceObject] = newInstance; // Добавляем в кэш перед рекурсией
                    CopyState(sourceObject, newInstance, typeMapper, processedObjects); // Рекурсивный вызов, передаем processedObjects
                    return newInstance;
                }
            }

            Console.WriteLine($"[StateCopier] Type '{remappedType.Name}' is not instantiable. State will be lost.");
            return null;
        }
    }
}