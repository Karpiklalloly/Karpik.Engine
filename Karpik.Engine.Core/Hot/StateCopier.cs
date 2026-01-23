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
            if (processedObjects.TryGetValue(sourceObject, out var existing)) return existing;

            var sourceObjectType = sourceObject.GetType();

            // 1. Примитивы и строки
            if (sourceObjectType.IsPrimitive || sourceObjectType.IsEnum || sourceObjectType == typeof(string))
            {
                return sourceObject;
            }

            // 2. Ремаппинг типа
            var remappedType = typeMapper.GetNewType(sourceObjectType);

            // Fallback для System типов, если ремаппинг не сработал или не нужен
            if (remappedType == null)
            {
                string asmName = sourceObjectType.Assembly.GetName().Name;
                // Если это системный тип (например, List<int>), но TypeMapper вернул null 
                // (значит generic аргументы не менялись), используем исходный тип.
                if (asmName.StartsWith("System") || asmName == "mscorlib" || asmName == "netstandard")
                {
                    // ОПАСНО: Если это List<OldType>, а TypeMapper вернул null, мы вернем старый список.
                    // Но TypeMapper должен был вернуть null только если не смог сконструировать тип.
                    // Для надежности лучше считать, что если remappedType == null для System коллекции, 
                    // которая содержит OldType, это ошибка. Но пока оставим как есть.
                    return sourceObject;
                }
                return null;
            }

            // 3. Обработка МАССИВОВ (Arrays)
            if (sourceObjectType.IsArray && remappedType.IsArray)
            {
                var sourceArray = (Array)sourceObject;
                var length = sourceArray.Length;
                // Создаем массив правильного типа и длины
                var newArray = Array.CreateInstance(remappedType.GetElementType()!, length);

                processedObjects[sourceObject] = newArray;

                for (int i = 0; i < length; i++)
                {
                    var val = ConvertObject(sourceArray.GetValue(i), typeMapper, processedObjects);
                    newArray.SetValue(val, i);
                }
                return newArray;
            }

            // 4. Обработка Коллекций (List, Dictionary, HashSet и др.)
            if (typeof(IEnumerable).IsAssignableFrom(remappedType) && remappedType != typeof(string))
            {
                // Пытаемся создать экземпляр
                object? newCollection = null;
                try { newCollection = Activator.CreateInstance(remappedType); } catch { }

                if (newCollection != null)
                {
                    processedObjects[sourceObject] = newCollection;

                    // 4.1 Словари
                    if (sourceObject is IDictionary oldDict && newCollection is IDictionary newDict)
                    {
                        foreach (DictionaryEntry entry in oldDict)
                        {
                            var newKey = ConvertObject(entry.Key, typeMapper, processedObjects);
                            var newValue = ConvertObject(entry.Value, typeMapper, processedObjects);
                            if (newKey != null)
                            {
                                try { newDict.Add(newKey, newValue); } catch { }
                            }
                        }
                    }
                    // 4.2 Списки (IList)
                    else if (sourceObject is IList oldList && newCollection is IList newList)
                    {
                        foreach (var item in oldList)
                        {
                            var newItem = ConvertObject(item, typeMapper, processedObjects);
                            // IList.Add возвращает int, игнорируем его
                            newList.Add(newItem);
                        }
                    }
                    // 4.3 Общие коллекции (HashSet<T> и прочие, где есть метод Add)
                    else
                    {
                        // Ищем метод Add через рефлексию, так как нет общего интерфейса для Add
                        var addMethod = remappedType.GetMethod("Add");
                        if (addMethod != null)
                        {
                            foreach (var item in (IEnumerable)sourceObject)
                            {
                                var newItem = ConvertObject(item, typeMapper, processedObjects);
                                try { addMethod.Invoke(newCollection, new[] { newItem }); } catch { }
                            }
                        }
                    }
                    return newCollection;
                }
            }

            // 5. Обычные объекты (Classes / Structs)
            // Проверяем конструктор без параметров (для структур он есть всегда неявно, но Activator работает)
            if (!remappedType.IsAbstract && !remappedType.IsInterface)
            {
                object? newInstance = null;
                try
                {
                    newInstance = Activator.CreateInstance(remappedType);
                }
                catch
                {
                    // Нет конструктора без параметров? Можно попробовать FormatterServices.GetUninitializedObject(remappedType)
                    // но это рискованно.
                    Console.WriteLine($"[StateCopier] Cannot create instance of {remappedType.Name}. No parameterless constructor?");
                }

                if (newInstance != null)
                {
                    processedObjects[sourceObject] = newInstance;
                    CopyState(sourceObject, newInstance, typeMapper, processedObjects);
                    return newInstance;
                }
            }

            return null;
        }
    }
}