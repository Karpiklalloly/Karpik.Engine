using System.Reflection;

namespace Karpik.Engine.Shared;

public static class DIExtensions
{
    public static void InjectProperties(this object obj, IServiceProvider serviceProvider)
    {
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        properties = properties
            .Where(p => p.IsDefined(typeof(DIAttribute), false) && p.CanWrite).ToArray();

        foreach (var prop in properties)
        {
            var service = serviceProvider.GetService(prop.PropertyType);
            if (service != null)
            {
                prop.SetValue(obj, service);
            }
        }
        
        var fields = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        fields = fields.Where(p => p.IsDefined(typeof(DIAttribute), false)).ToArray();

        foreach (var fieldInfo in fields)
        {
            var service = serviceProvider.GetService(fieldInfo.FieldType);
            if (service != null)
            {
                fieldInfo.SetValue(obj, service);
            }
        }
    }

    public static void Inject(this IServiceProvider serviceProvider, object obj)
    {
        obj.InjectProperties(serviceProvider);
    }
    
    public static T Create<T>(this IServiceProvider serviceProvider) where T : class, new()
    {
        var obj = new T();
        obj.InjectProperties(serviceProvider);
        return obj;
    }
}