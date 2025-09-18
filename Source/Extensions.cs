using System.Diagnostics.CodeAnalysis;
using Hqub.MusicBrainz;

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

    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IReadOnlyList<T>? list) => list is null || list.Count == 0;
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this QueryResult<T>? list) => list is null || list.Count == 0;

    public static string Quote(this string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        if (s.IndexOf(' ') < 0)
        {
            return s;
        }

        return "\"" + s + "\"";
    }
}
