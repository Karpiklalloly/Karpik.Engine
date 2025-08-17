namespace Karpik.Engine.Client.UIToolkit;

public class Selector : IComparable<Selector>
{
    public string Raw { get; }
    public (int Ids, int Classes) Specificity { get; }

    public Selector(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !(raw.StartsWith('.') || raw.StartsWith('#')))
        {
            throw new ArgumentException("Selector must start with '.' for a class or '#' for an ID.");
        }
        Raw = raw;
        Specificity = CalculateSpecificity(raw);
    }
    
    private static (int, int) CalculateSpecificity(string s)
    {
        if (s.StartsWith('#')) return (1, 0);
        if (s.StartsWith('.')) return (0, 1);
        return (0, 0); // Недостижимо из-за проверки в конструкторе
    }

    // Сравнение стало проще
    public int CompareTo(Selector? other)
    {
        if (other is null) return 1;
        if (Specificity.Ids != other.Specificity.Ids) 
            return Specificity.Ids.CompareTo(other.Specificity.Ids);
        return Specificity.Classes.CompareTo(other.Specificity.Classes);
    }
}