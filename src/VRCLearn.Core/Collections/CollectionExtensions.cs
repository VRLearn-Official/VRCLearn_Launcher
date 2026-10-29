namespace VRCLearn.Core.Collections;

public static class CollectionExtensions
{
    public static bool Toggle(this ICollection<string> items, string value)
    {
        if (items.Remove(value))
        {
            return true;
        }

        items.Add(value);

        return false;
    }
}
