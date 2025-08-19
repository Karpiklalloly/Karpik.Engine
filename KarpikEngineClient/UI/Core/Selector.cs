namespace Karpik.Engine.Client.UIToolkit;

public class Selector : IComparable<Selector>
{
    public string Raw { get; }
    // Специфичность теперь (IDs, Classes, PseudoClasses)
    public (int Ids, int Classes, int PseudoClasses) Specificity { get; }

    public Selector(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("Selector cannot be empty.");
        }
        Raw = raw;
        Specificity = CalculateSpecificity(raw);
    }
    
    private static (int, int, int) CalculateSpecificity(string s)
    {
        int ids = 0;
        int classes = 0;
        int pseudoClasses = 0;

        // Простое разделение для примера. Более сложный парсер мог бы обрабатывать комбинации.
        if (s.Contains(':')) pseudoClasses++;
        if (s.StartsWith('#')) ids++;
        else if (s.StartsWith('.')) classes++;
        
        return (ids, classes, pseudoClasses);
    }

    public int CompareTo(Selector? other)
    {
        if (other is null) return 1;
        if (Specificity.Ids != other.Specificity.Ids) 
            return Specificity.Ids.CompareTo(other.Specificity.Ids);
        if (Specificity.Classes != other.Specificity.Classes)
            return Specificity.Classes.CompareTo(other.Specificity.Classes);
        return Specificity.PseudoClasses.CompareTo(other.Specificity.PseudoClasses);
    }
}