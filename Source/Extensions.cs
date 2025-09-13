static class Extensions
{
    public static string TrimStart(this string v, string value, StringComparison comparison = StringComparison.InvariantCultureIgnoreCase)
    {
        return v.StartsWith(value, comparison) ? v[value.Length..] : v;
    }

    public static string TrimEnd(this string v, string value, StringComparison comparison = StringComparison.InvariantCultureIgnoreCase)
    {
        return v.EndsWith(value, comparison) ? v[..^value.Length] : v;
    }
}