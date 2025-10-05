using Hqub.MusicBrainz.Entities;

namespace YtPlaylist;

static class MusicBrainzUtils
{
    const int IndentSize = 2;

    static void Print(string property, string? value, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));
        Console.WriteLine($"{property}: {value}");
    }

    static void Print(string property, int value, int depth)
    {
        Console.Write(new string(' ', depth * IndentSize));
        Console.WriteLine($"{property}: {value}");
    }

    static void Print<T>(string property, T? value, Action<T, int> print, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));
        Console.WriteLine($"{property}:");
        print.Invoke(value, depth + 1);
    }

    static void Print<T>(string property, IEnumerable<T>? value, Action<T, int> print, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));
        Console.WriteLine($"{property}:");
        foreach (T? item in value)
        {
            print.Invoke(item, depth + 1);
        }
    }

    public static void Print(Release release, int depth = 0)
    {
        Print("Title", release.Title, depth);
        Print("Date", release.Date, depth);
        Print("Status", release.Status, depth);
        Print("Media", release.Media, Print, depth);
        Print("Credits", release.Credits, Print, depth);
    }

    public static void Print(NameCredit credit, int depth = 0)
    {
        Print("Name", credit.Name, depth);
        Print("Artist", credit.Artist, Print, depth);
    }

    public static void Print(Medium medium, int depth = 0)
    {
        Print("Format", medium.Format, depth);
        Print("TrackCount", medium.TrackCount, depth);
        Print("Tracks", medium.Tracks, Print, depth);
    }

    public static void Print(Track track, int depth = 0)
    {
        Print("Position", track.Position, depth);
        Print("Number", track.Number, depth);
        Print("Length", track.Length is null ? null : TimeSpan.FromMilliseconds(track.Length.Value).ToString(), depth);
    }

    public static void Print(Artist artist, int depth = 0)
    {
        Print("Name", artist.Name, depth);
        Print("Aliases", artist.Aliases, Print, depth);
    }

    public static void Print(Alias alias, int depth = 0)
    {
        Print("Name", alias.Name, depth);
    }
}
