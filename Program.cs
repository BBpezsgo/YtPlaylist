using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Hqub.MusicBrainz;
using Logger;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;

namespace YtPlaylist;

static class Program
{
    [NotNull] static string? PlaylistId = null;
    [NotNull] static string? OutputPath = null;
    const int MaxRetries = 4;
    const int MaxConcurrency = 1;

    static int Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p" or "--playlist":
                    if (PlaylistId is not null)
                    {
                        Log.Error($"Playlist id already defined");
                        return 1;
                    }

                    if (i + 1 == args.Length)
                    {
                        Log.Error($"Expected a playlist id after the argument {args[i]}");
                        return 1;
                    }

                    PlaylistId = args[++i];
                    break;
                case "-o" or "--output":
                    if (OutputPath is not null)
                    {
                        Log.Error($"Output directory already defined");
                        return 1;
                    }

                    if (i + 1 == args.Length)
                    {
                        Log.Error($"Expected an output directory after the argument {args[i]}");
                        return 1;
                    }

                    OutputPath = args[++i];
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

        if (string.IsNullOrEmpty(PlaylistId))
        {
            Log.Error($"Playlist id not defined.");
            return 1;
        }

        if (string.IsNullOrEmpty(OutputPath))
        {
            Log.Error($"Output directory not defined.");
            return 1;
        }

        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;

        CancellationTokenSource cancellationTokenSource = new();

        Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        using Task task = Run(cancellationTokenSource.Token);

        while (!task.IsCompleted)
        {
            Thread.Yield();
        }

        if (task.Exception is not null)
        {
            Log.Error(task.Exception);
        }

        return 0;
    }

    static async Task Run(CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> onDisk = [];
        HashSet<string> online = [];

        YoutubeClient youtube = new();

        MusicBrainzClient musicBrainz = new(new HttpClient(new SocketsHttpHandler()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        })
        {
            DefaultRequestHeaders = { { "User-Agent", "Hqub.MusicBrainz/3.0 (https://github.com/avatar29A/MusicBrainz)" } },
            BaseAddress = new Uri("https://musicbrainz.org/ws/2/"),
        })
        {
            Cache = new FileRequestCache("./cache")
            {
                Timeout = TimeSpan.FromDays(30),
            },
        };

        Log.MinorAction("Checking files");

        foreach (string filename in Directory.GetFiles(OutputPath, "*.mp3"))
        {
            if (cancellationToken.IsCancellationRequested) return;
            TagLib.File file = TagLib.File.Create(filename);
            string name = Path.GetFileNameWithoutExtension(filename);
            string[] parts = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            bool modified = false;

            if (parts.Length == 2)
            {
                string[] artists = parts[0].Split('&', StringSplitOptions.TrimEntries);
                string title = parts[1];

                if (!file.Tag.Performers.SequenceEqual(artists))
                {
                    Log.None($"Artists fixed: `{string.Join(" & ", file.Tag.Performers)}` --> `{string.Join(" & ", artists)}`");
                    file.Tag.Performers = artists;
                    modified = true;
                }

                if (file.Tag.Title != title)
                {
                    Log.None($"Title fixed: `{file.Tag.Title}` --> `{title}`");
                    file.Tag.Title = title;
                    modified = true;
                }
            }
            else
            {
                Log.Warning($"Invalid filename `{name}`");
            }

            //if (await MusicBrainz.FetchMetadata(file, musicBrainz, cancellationToken))
            //{
            //    modified = true;
            //}

            if (modified)
            {
                file.Save();
            }

            if (!string.IsNullOrWhiteSpace(file.Tag.Description))
            {
                onDisk.Add(file.Tag.Description, filename);
            }
            else
            {
                Log.Warning($"Unexpected file {Path.GetFileName(filename)}");
            }
        }

        Log.MinorAction("Fetching playlist");

        Playlist playlist = await youtube.Playlists.GetAsync($"https://youtube.com/playlist?list={PlaylistId}", cancellationToken);

        await DownloadVideos(youtube.Playlists.GetVideosAsync(playlist.Url, cancellationToken), youtube, musicBrainz, onDisk, online, cancellationToken);

        List<string> deleteFiles = [];
        foreach ((string id, string filename) in onDisk)
        {
            if (!online.Contains(id))
            {
                deleteFiles.Add(filename);
            }
        }

        if (deleteFiles.Count > 0)
        {
            foreach (string filename in deleteFiles)
            {
                Log.Warning($"Music file \"{Path.GetFileNameWithoutExtension(filename)}\" shouldn't be here");
            }

            if (Log.AskYesNo("Do you want to delete the files above?", true))
            {
                foreach (string filename in deleteFiles)
                {
                    File.Delete(filename);
                }
            }
        }
    }

    static async Task DownloadVideos(IAsyncEnumerable<PlaylistVideo> videos, YoutubeClient youtube, MusicBrainzClient musicBrainz, Dictionary<string, string> onDisk, HashSet<string> online, CancellationToken cancellationToken = default)
    {
        Channel<PlaylistVideo> channel = Channel.CreateUnbounded<PlaylistVideo>();

        Task[] tasks = new Task[1 + MaxConcurrency];

        tasks[0] = Task.Run(async () =>
        {
            Log.MinorAction("Fetching videos");
            await foreach (PlaylistVideo video in videos)
            {
                online.Add(video.Id);
                if (onDisk.ContainsKey(video.Id)) continue;
                await channel.Writer.WriteAsync(video, cancellationToken);
            }
            channel.Writer.Complete();
            Log.MinorAction("All videos fetched");
        }, cancellationToken);

        for (int i = 0; i < MaxConcurrency; i++)
        {
            tasks[i + 1] = DownloadVideosJob(youtube, musicBrainz, channel, cancellationToken);
        }

        await Task.WhenAll(tasks);
    }

    static async Task DownloadVideosJob(YoutubeClient youtube, MusicBrainzClient musicBrainz, Channel<PlaylistVideo> channel, CancellationToken cancellationToken = default)
    {
        await foreach (PlaylistVideo video in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            Log.MinorAction($"Downloading \e[1m{video.Title}\e[22m");

            await DownloadVideo(youtube, musicBrainz, video, cancellationToken);
        }
    }

    static async Task DownloadVideo(YoutubeClient youtube, MusicBrainzClient musicBrainz, PlaylistVideo video, CancellationToken cancellationToken = default)
    {
        HttpRequestException? lastException = null;

        for (int retry = 1; retry <= MaxRetries; retry++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                await DownloadVideoJob(youtube, musicBrainz, video, cancellationToken);
                return;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;

                if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    await Task.Delay(1000, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) return;
                Log.Error($"Failed to download {video.Title}:");
                Log.Error(ex);
                return;
            }
        }

        if (cancellationToken.IsCancellationRequested) return;
        if (lastException is null) return;

        if (lastException.StatusCode.HasValue)
        {
            Log.Error($"Failed to download {video.Title}: HTTP {(int)lastException.StatusCode} ({lastException.StatusCode})");
        }
        else
        {
            Log.Error(lastException);
        }
    }

    static async Task DownloadAudioData(string filename, YoutubeClient youtube, string url, CancellationToken cancellationToken = default)
    {
        using (Process process = Process.Start(new ProcessStartInfo()
        {
            FileName = "yt-dlp",
            Arguments = $"-o \"{filename}\" -x --audio-format mp3 {url}",
            UseShellExecute = true,
        })!)
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception($"yt-dlp exited with code {process.ExitCode}");
            }
            Console.WriteLine("OK");
            return;
        }

        StreamManifest streamManifest = await youtube.Videos.Streams.GetManifestAsync(url, cancellationToken);

        IStreamInfo? bestAudioStream = streamManifest.GetAudioStreams().GetWithHighestBitrate();

        await youtube.Videos.DownloadAsync(
            [bestAudioStream],
            new ConversionRequestBuilder(filename).Build(),
            null,
            cancellationToken
        );
    }

    static async Task DownloadVideoJob(YoutubeClient youtube, MusicBrainzClient musicBrainz, PlaylistVideo video, CancellationToken cancellationToken = default)
    {
        (string artist, string title) = YouTube.NormalizeMetadata(video);

        string filename = Path.Combine(OutputPath, $"{artist.Replace("/", "_").Replace("\\", "_")} - {title.Replace("/", "_").Replace("\\", "_")}.mp3");

        await DownloadAudioData(filename, youtube, $"https://www.youtube.com/watch?v={video.Id}", cancellationToken);

        TagLib.File file = TagLib.File.Create(filename);

        await YouTube.FetchMetadata(file, video, cancellationToken);
        await MusicBrainz.FetchMetadata(file, musicBrainz, cancellationToken);

        file.RemoveTags(file.TagTypes & ~file.TagTypesOnDisk);
        file.Save();
    }
}
