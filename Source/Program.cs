using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Logger;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;

static class Program
{
    [NotNull] static string? PlaylistId = null;
    [NotNull] static string? OutputPath = null;
    const int MaxRetries = 4;
    const int MaxConcurrency = 2;

    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("YouTube playlist downloader");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine("YtPlaylist <playlist id> <destination directory>");
            return;
        }

        PlaylistId = args[0];
        OutputPath = args[1];

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
    }

    static async Task Run(CancellationToken cancellationToken = default)
    {
        HashSet<string> onDisk = [];
        HashSet<string> downloaded = [];

        Log.MinorAction("Checking files");

        foreach (string filename in Directory.GetFiles(OutputPath, "*.mp3"))
        {
            TagLib.File file = TagLib.File.Create(filename);
            string name = Path.GetFileNameWithoutExtension(filename);
            string[] parts = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                string[] artists = parts[0].Split('&', StringSplitOptions.TrimEntries);
                string title = parts[1];

                if (!file.Tag.Performers.SequenceEqual(artists))
                {
                    Log.Warning($"Artists fixed: \"{string.Join(" & ", file.Tag.Performers)}\" --> \"{string.Join(" & ", artists)}\"");
                }
                file.Tag.Performers = artists;

                if (file.Tag.Title != title)
                {
                    Log.Warning($"Title fixed: \"{file.Tag.Title}\" --> \"{title}\"");
                }
                file.Tag.Title = title;
            }
            else
            {
                Log.Warning($"Invalid filename \"{name}\"");
            }
            file.Save();

            if (!string.IsNullOrWhiteSpace(file.Tag.Description))
            {
                onDisk.Add(file.Tag.Description);
            }
            else
            {
                Log.Warning($"Unexpected file {Path.GetFileName(filename)}");
            }
        }

        YoutubeClient youtube = new();

        Log.MinorAction("Fetching playlist");

        Playlist playlist = await youtube.Playlists.GetAsync($"https://youtube.com/playlist?list={PlaylistId}", cancellationToken);

        Log.MajorAction($"Downloading playlist \e[1m{playlist.Title}\e[22m");

        await DownloadVideos(youtube.Playlists.GetVideosAsync(playlist.Url, cancellationToken), youtube, onDisk, downloaded, cancellationToken);
    }

    static async Task DownloadVideos(IAsyncEnumerable<PlaylistVideo> videos, YoutubeClient youtube, HashSet<string> onDisk, HashSet<string> downloaded, CancellationToken cancellationToken = default)
    {
        Channel<PlaylistVideo> channel = Channel.CreateUnbounded<PlaylistVideo>();

        Task[] tasks = new Task[1 + MaxConcurrency];

        tasks[0] = Task.Run(async () =>
        {
            Log.MinorAction("Fetching videos");
            await foreach (PlaylistVideo video in videos)
            {
                if (onDisk.Contains(video.Id)) continue;
                await channel.Writer.WriteAsync(video, cancellationToken);
            }
            channel.Writer.Complete();
            Log.MinorAction("All videos fetched");
        }, cancellationToken);

        for (int i = 0; i < MaxConcurrency; i++)
        {
            tasks[i + 1] = DownloadVideosJob(youtube, channel, cancellationToken);
        }

        await Task.WhenAll(tasks);
    }

    static async Task DownloadVideosJob(YoutubeClient youtube, Channel<PlaylistVideo> channel, CancellationToken cancellationToken = default)
    {
        await foreach (PlaylistVideo video in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            Log.MinorAction($"Downloading \e[1m{video.Title}\e[22m");

            await DownloadVideo(youtube, video, cancellationToken);
        }
    }

    static async Task DownloadVideo(YoutubeClient youtube, PlaylistVideo video, CancellationToken cancellationToken = default)
    {
        HttpRequestException? lastException = null;

        for (int retry = 1; retry <= MaxRetries; retry++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                await DownloadVideoJob(youtube, video, cancellationToken);
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

    static async Task DownloadVideoJob(YoutubeClient youtube, PlaylistVideo video, CancellationToken cancellationToken = default)
    {
        (string author, string title) = NormalizeMetadata(video);

        StreamManifest streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Url, cancellationToken);

        IStreamInfo? bestAudioStream = streamManifest.GetAudioStreams().GetWithHighestBitrate();

        string filename = Path.Combine(OutputPath, $"{author.Replace("/", "_").Replace("\\", "_")} - {title.Replace("/", "_").Replace("\\", "_")}.mp3");

        await youtube.Videos.DownloadAsync(
            [bestAudioStream],
            new ConversionRequestBuilder(filename).Build(),
            null,
            cancellationToken
        );

        byte[] imageBytes;
        using (HttpClient client = new())
        {
            imageBytes = await client.GetByteArrayAsync(video.Thumbnails.OrderByDescending(v => v.Resolution.Area).First().Url, cancellationToken);
        }

        TagLib.File file = TagLib.File.Create(filename);

        TagLib.Id3v2.AttachmentFrame cover = new()
        {
            Type = TagLib.PictureType.FrontCover,
            Description = "Cover",
            MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg,
            Data = imageBytes,
            TextEncoding = TagLib.StringType.UTF16
        };
        file.Tag.Pictures = [cover];

        file.Tag.Title = title;
        file.Tag.Performers = author.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        file.Tag.Description = video.Id;
        file.RemoveTags(file.TagTypes & ~file.TagTypesOnDisk);
        file.Save();
    }

    static (string Author, string Title) NormalizeMetadata(PlaylistVideo video)
    {
        string author = video.Author.ChannelTitle;
        string title = video.Title;

        if (title.StartsWith(author.ToLowerInvariant() + " - ", StringComparison.InvariantCultureIgnoreCase))
        {
            title = title[(author.Length + 3)..].TrimStart();
        }

        author = author.TrimEnd(" - Topic").TrimEnd();

        return (author, title);
    }
}
