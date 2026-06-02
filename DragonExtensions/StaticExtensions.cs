namespace DragonExtensions;

public static class StaticExtensions
{
    extension(EcsSpan span)
    {
        public bool Has(int id)
        {
            for (int i = 0; i < span.Count; i++)
            {
                if (span[i] == id) return true;
            }

            return false;
        }
    }
}