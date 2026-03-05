using System.Collections.Immutable;
using Logger;

namespace YtPlaylist;

static class Program
{
    static int Main(string[] args)
    {
#if DEBUG
        args = [
            "--playlist",
            "PL3pKDp-F7PPtqyA3Q_F8lpLohgbZnOAiU",
            "--playlist",
            "PL3pKDp-F7PPuo3MIneE9MX77zKcEiw-QZ",
            "--playlist",
            "PL3pKDp-F7PPuI_BsyPZfXtNySJ5By-Yrb",
            "--playlist",
            "PL3pKDp-F7PPu785eiO43ccKgaOCLhpTBJ",
            "--output",
            //"/home/bb/Android/Internal storage/Music",
            "/d1/Music",
            "--httpcache",
            "/home/bb/Projects/YtPlaylist/cache",
        ];
#endif

        List<string> playlistIds = [];
        string? outputPath = null;
        bool useCache = true;
        bool dryRun = false;
        bool download = true;
        bool metadata = true;
        bool lyrics = true;
        string? httpCachePath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p" or "--playlist":
                    if (i + 1 == args.Length)
                    {
                        Log.Error($"Expected a playlist id after the argument {args[i]}");
                        return 1;
                    }

                    playlistIds.Add(args[++i]);
                    break;
                case "-o" or "--output":
                    if (outputPath is not null)
                    {
                        Log.Error($"Output directory already defined");
                        return 1;
                    }

                    if (i + 1 == args.Length)
                    {
                        Log.Error($"Expected an output directory after the argument {args[i]}");
                        return 1;
                    }

                    outputPath = args[++i];
                    break;
                case "--nocache":
                    useCache = false;
                    break;
                case "--dry":
                    dryRun = true;
                    break;
                case "--nodownload":
                    download = false;
                    break;
                case "--nometadata":
                    metadata = false;
                    break;
                case "--nolyrics":
                    lyrics = false;
                    break;
                case "--httpcache":
                    if (httpCachePath is not null)
                    {
                        Log.Error($"HTTP cache path already defined");
                        return 1;
                    }

                    if (i + 1 == args.Length)
                    {
                        Log.Error($"Expected a path name after the argument {args[i]}");
                        return 1;
                    }

                    httpCachePath = args[++i];
                    break;
                default:
                    Log.Error($"Unexpected argument {args[i]}");
                    return 1;
            }
        }

        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            Console.WriteLine("YouTube Playlist Downloader");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine("YtPlaylist <-p|--playlist Playlist Id> <-o|--output Output Directory>");
            return 1;
        }

        if (playlistIds.Count == 0)
        {
            Log.Error($"No playlist specified");
            return 1;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            Log.Error($"Output directory not specified");
            return 1;
        }

        if (!Directory.Exists(outputPath))
        {
            Log.Error($"Output directory doesn't exists");
            return 1;
        }

        CancellationTokenSource cancellationTokenSource = new();

        Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        new App()
        {
            Arguments = new AppArguments()
            {
                PlaylistIds = [.. playlistIds],
                UseCache = useCache,
                DryRun = dryRun,
                Download = download,
                Metadata = metadata,
                Lyrics = lyrics,
                OutputPath = outputPath,
                HttpCachePath = httpCachePath ?? "./cache",
            },
        }.Run(cancellationTokenSource.Token).ContinueWith(task =>
        {
            if (task.Exception is not null)
            {
                foreach (Exception item in task.Exception.Flatten().InnerExceptions)
                {
                    Log.Error(item);
                }
            }
        }).Wait();

        return 0;
    }
}
