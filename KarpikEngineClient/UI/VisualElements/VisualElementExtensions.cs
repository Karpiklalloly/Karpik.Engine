namespace Karpik.Engine.Client.UIToolkit;

public static class VisualElementExtensions
{
    public static T Q<T>(this VisualElement element) where T : VisualElement
    {
        return (T)element.Children.First(x => x.GetType() == typeof(T));
    }
    
    public static T Q<T>(this VisualElement element, string name) where T : VisualElement
    {
        return (T)element.Children.First(x =>
        {
            if (x.Name != name) return false;
            if (x.GetType() != typeof(T)) return false;
            
            return true;
        });
    }
    
    public static IEnumerable<T> Qs<T>(this VisualElement element) where T : VisualElement
    {
        return element.Children.Where(x => x.GetType() == typeof(T)).Cast<T>();
    }
    
    public static IEnumerable<T> Qs<T>(this VisualElement element, string name) where T : VisualElement
    {
        return element.Children.Where(x =>
        {
            if (x.Name != name) return false;
            if (x.GetType() != typeof(T)) return false;
            
            return true;
        }).Cast<T>();
    }
    
    // public static T DeepQ<T>(this VisualElement element) where T : VisualElement
    // {
    //     return (T)element.AllChildren.First(x => x.GetType() == typeof(T));
    // }
    //
    // public static T DeepQ<T>(this VisualElement element, string name) where T : VisualElement
    // {
    //     return (T)element.AllChildren.First(x =>
    //     {
    //         if (x.Name != name) return false;
    //         if (x.GetType() != typeof(T)) return false;
    //         
    //         return true;
    //     });
    // }
    //
    // public static IEnumerable<T> DeepQs<T>(this VisualElement element) where T : VisualElement
    // {
    //     return element.AllChildren.Where(x => x.GetType() == typeof(T)).Cast<T>();
    // }
    //
    // public static IEnumerable<T> DeepQs<T>(this VisualElement element, string name) where T : VisualElement
    // {
    //     return element.AllChildren.Where(x =>
    //     {
    //         if (x.Name != name) return false;
    //         if (x.GetType() != typeof(T)) return false;
    //         
    //         return true;
    //     }).Cast<T>();
    // }
}