using Hqub.MusicBrainz.Entities;

namespace YtPlaylist;

static class MusicBrainzUtils
{
    const int IndentSize = 2;

    static void Print(string property, string? value, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{property}: ");
        Console.ResetColor();

        Console.WriteLine(value);
    }

    static void Print(string property, int? value, int depth)
    {
        if (!value.HasValue) return;
        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{property}: ");
        Console.ResetColor();

        Console.WriteLine(value.Value);
    }

    static void Print(string property, int value, int depth)
    {
        if (value == default) return;

        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{property}: ");
        Console.ResetColor();

        Console.WriteLine(value);
    }

    static void Print(string property, double? value, int depth)
    {
        if (!value.HasValue) return;
        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{property}: ");
        Console.ResetColor();

        Console.WriteLine(value.Value);
    }

    static void Print(string property, double value, int depth)
    {
        if (value == default) return;

        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{property}: ");
        Console.ResetColor();

        Console.WriteLine(value);
    }

    static void Print<T>(string property, T? value, Action<T, int> print, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"{property}: ");
        Console.ResetColor();

        print.Invoke(value, depth + 1);
    }

    static void Print<T>(string property, List<T>? value, Action<T, int> print, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"{property}: ");
        Console.ResetColor();

        if (value.Count == 1)
        {
            print.Invoke(value[0], depth + 1);
        }
        else
        {
            for (int i = 0; i < value.Count; i++)
            {
                Console.Write(new string(' ', (depth + 1) * IndentSize));
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"{i}: ");
                Console.ResetColor();
                print.Invoke(value[i], depth + 2);
            }
        }
    }

    static void Print<T>(string property, List<T>? value, int depth)
    {
        if (value is null) return;
        Console.Write(new string(' ', depth * IndentSize));

        if (value.Count == 1)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"{property}: ");
            Console.ResetColor();

            Console.WriteLine(value[0]);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{property}: ");
            Console.ResetColor();

            for (int i = 0; i < value.Count; i++)
            {
                Console.Write(new string(' ', (depth + 1) * IndentSize));
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"{i}: ");
                Console.ResetColor();

                Console.WriteLine(value[i]?.ToString() ?? string.Empty, depth + 2);
            }
        }
    }

    public static void Print(Recording recording, int depth = 0)
    {
        Print("Title", recording.Title, depth);
        Print("Aliases", recording.Aliases, Print, depth);
        Print("Credits", recording.Credits, Print, depth);
        Print("Disambiguation", recording.Disambiguation, depth);
        Print("Genres", recording.Genres, Print, depth);
        Print("Length", recording.Length, depth);
        Print("Rating", recording.Rating, Print, depth);
        Print("Releases", recording.Releases, Print, depth);
        Print("Tags", recording.Tags, Print, depth);
    }

    public static void Print(Rating rating, int depth = 0)
    {
        Print("Value", rating.Value, depth);
        Print("VotesCount", rating.VotesCount, depth);
    }

    public static void Print(Genre genre, int depth = 0)
    {
        Print("Name", genre.Name, depth);
        Print("Count", genre.Count, depth);
        Print("Disambiguation", genre.Disambiguation, depth);
    }

    public static void Print(Tag tag, int depth = 0)
    {
        Print("Name", tag.Name, depth);
        Print("Count", tag.Count, depth);
    }

    public static void Print(Release release, int depth = 0)
    {
        Print("Title", release.Title, depth);
        Print("ReleaseGroup", release.ReleaseGroup, Print, depth);
        Print("Date", release.Date, depth);
        Print("Barcode", release.Barcode, depth);
        Print("Country", release.Country, depth);
        Print("Genres", release.Genres, Print, depth);
        Print("Labels", release.Labels, Print, depth);
        Print("Tags", release.Tags, Print, depth);
        Print("Quality", release.Quality, depth);
        Print("Score", release.Score, depth);
        Print("Status", release.Status, depth);
        Print("Media", release.Media, Print, depth);
        Print("Credits", release.Credits, Print, depth);
    }

    public static void Print(ReleaseGroup releaseGroup, int depth = 0)
    {
        Print("Title", releaseGroup.Title, depth);
        Print("Aliases", releaseGroup.Aliases, Print, depth);
        Print("Credits", releaseGroup.Credits, Print, depth);
        Print("FirstReleaseDate", releaseGroup.FirstReleaseDate, depth);
        Print("Genres", releaseGroup.Genres, Print, depth);
        Print("PrimaryType", releaseGroup.PrimaryType, depth);
        Print("Rating", releaseGroup.Rating, Print, depth);
        Print("Score", releaseGroup.Score, depth);
        Print("SecondaryTypes", releaseGroup.SecondaryTypes, depth);
        Print("Tags", releaseGroup.Tags, Print, depth);
    }

    public static void Print(LabelInfo labelInfo, int depth = 0)
    {
        Print("Name", labelInfo.Label, Print, depth);
        Print("CatalogNumber", labelInfo.CatalogNumber, depth);
    }

    public static void Print(Label label, int depth = 0)
    {
        Print("Name", label.Name, depth);
        Print("Disambiguation", label.Disambiguation, depth);
        Print("Aliases", label.Aliases, Print, depth);
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
        Print("Position", medium.Position, depth);
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
