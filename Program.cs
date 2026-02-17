using Logger;

namespace YtPlaylist;

static class Program
{
    static int Main(string[] args)
    {

        string? playlistId = null;
        string? outputPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p" or "--playlist":
                    if (playlistId is not null)
                    {
                        Log.Error($"Playlist id already defined");
                        return 1;
                    }

                    if (i + 1 == args.Length)
                    {
                        Log.Error($"Expected a playlist id after the argument {args[i]}");
                        return 1;
                    }

                    playlistId = args[++i];
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

        if (string.IsNullOrEmpty(playlistId))
        {
            Log.Error($"Playlist id not defined.");
            return 1;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            Log.Error($"Output directory not defined.");
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
            OutputPath = outputPath,
            PlaylistId = playlistId,
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
