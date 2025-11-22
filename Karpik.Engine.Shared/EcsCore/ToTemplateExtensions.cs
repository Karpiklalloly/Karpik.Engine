using System.Collections.Concurrent;
using System.Reflection;

namespace Karpik.Engine.Shared;

public static class ToTemplateExtensions
{
    // Кэш для созданных типов ComponentTemplate<T>
    private static readonly ConcurrentDictionary<Type, Type> ComponentTemplateTypeCache = new();
    // Кэш для конструкторов ComponentTemplate<T>(T)
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> ConstructorCache = new();

    public static ComponentTemplateBase ToComponentTemplate(this IEcsComponent component)
    {
        var componentType = component.GetType();

        try
        {
            if (!ComponentTemplateTypeCache.TryGetValue(componentType, out var genericTemplateType))
            {
                var genericTypeDefinition = typeof(ComponentTemplate<>);
                genericTemplateType = genericTypeDefinition.MakeGenericType(componentType);
                ComponentTemplateTypeCache.TryAdd(componentType, genericTemplateType);
            }

            if (!ConstructorCache.TryGetValue(genericTemplateType, out var constructor))
            {
                constructor = genericTemplateType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null,
                    [componentType], null);

                if (constructor == null)
                {
                    return null;
                }
                ConstructorCache.TryAdd(genericTemplateType, constructor);
            }

            return (ComponentTemplateBase)constructor.Invoke([component]); // Приводим к базовому типу
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ToComponentTemplateReflection] Error creating template for {componentType.FullName}: {ex}");
            if (ex.InnerException != null)
            {
                 Console.WriteLine($"Inner Exception: {ex.InnerException}");
            }
            return null;
        }
    }
}

public static class ToTemplateExtensions2
{
    // Кэш для созданных типов ComponentTemplate<T>
    private static readonly ConcurrentDictionary<Type, Type> ComponentTemplateTypeCache = new();
    // Кэш для конструкторов ComponentTemplate<T>(T)
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> ConstructorCache = new();
    
    public static ComponentTemplateBase ToComponentTemplate(this IEcsTagComponent component)
    {
        var componentType = component.GetType();

        try
        {
            if (!ComponentTemplateTypeCache.TryGetValue(componentType, out var genericTemplateType))
            {
                var genericTypeDefinition = typeof(TagComponentTemplate<>);
                genericTemplateType = genericTypeDefinition.MakeGenericType(componentType);
                ComponentTemplateTypeCache.TryAdd(componentType, genericTemplateType);
            }

            if (!ConstructorCache.TryGetValue(genericTemplateType, out var constructor))
            {
                constructor = genericTemplateType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null,
                    [componentType], null);

                if (constructor == null)
                {
                    return null;
                }
                ConstructorCache.TryAdd(genericTemplateType, constructor);
            }

            return (ComponentTemplateBase)constructor.Invoke([component]); // Приводим к базовому типу
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ToComponentTemplateReflection] Error creating template for {componentType.FullName}: {ex}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException}");
            }
            return null;
        }
    }
}