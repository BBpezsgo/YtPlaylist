using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Hqub.MusicBrainz;
using Logger;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;
using System.Diagnostics;
using YoutubeExplode.Common;

namespace YtPlaylist;

class App
{
    public required string PlaylistId;
    public required string OutputPath;
    const int MaxRetries = 4;
    const int MaxConcurrency = 1;

    public async Task Run(CancellationToken cancellationToken = default)
    {
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;

        Dictionary<string, string> onDisk = [];
        HashSet<string> online = [];

        using YoutubeClient youtube = new();

        using MusicBrainzClient musicBrainz = new(new HttpClient(new SocketsHttpHandler()
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
            ReadOnlySpan<string> parts = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            bool modified = false;

            string[] artists;
            string title;

            switch (parts.Length)
            {
                case 2:
                    artists = parts[0].Split('&', StringSplitOptions.TrimEntries);
                    title = parts[1];
                    break;
                case > 2:
                    Log.Info($"Invalid filename `{name}`");
                    artists = parts[0].Split('&', StringSplitOptions.TrimEntries);
                    title = string.Join(" - ", parts[1..]);
                    break;
                default:
                    Log.Warning($"Invalid filename `{name}`");
                    continue;
            }

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

        await DownloadVideos(youtube.Playlists.GetVideoBatchesAsync(playlist.Url, cancellationToken), youtube, musicBrainz, onDisk, online, cancellationToken);

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

    Task DownloadVideos(IAsyncEnumerable<Batch<PlaylistVideo>> videos, YoutubeClient youtube, MusicBrainzClient musicBrainz, Dictionary<string, string> onDisk, HashSet<string> online, CancellationToken cancellationToken = default)
    {
        Channel<PlaylistVideo> channel = Channel.CreateUnbounded<PlaylistVideo>();

        Task[] tasks = new Task[1 + MaxConcurrency];

        tasks[0] = Task.Run(async () =>
        {
            Log.MinorAction("Fetching videos");
            await foreach (Batch<PlaylistVideo> batch in videos)
            {
                foreach (PlaylistVideo video in batch.Items)
                {
                    online.Add(video.Id);
                    if (onDisk.ContainsKey(video.Id)) continue;
                    await channel.Writer.WriteAsync(video, cancellationToken);
                }
                Log.Debug("Fetching next batch");
            }
            channel.Writer.Complete();
            Log.MinorAction("All videos fetched");
        }, cancellationToken);

        for (int i = 0; i < MaxConcurrency; i++)
        {
            tasks[i + 1] = DownloadVideosJob(youtube, musicBrainz, channel, cancellationToken);
        }

        return Task.WhenAll(tasks);
    }

    async Task DownloadVideosJob(YoutubeClient youtube, MusicBrainzClient musicBrainz, Channel<PlaylistVideo> channel, CancellationToken cancellationToken = default)
    {
        await foreach (PlaylistVideo video in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            Log.MinorAction($"Downloading \e[1m{video.Title}\e[22m");

            await DownloadVideoJobRetries(youtube, musicBrainz, video, cancellationToken);
        }
    }

    async Task DownloadVideoJobRetries(YoutubeClient youtube, MusicBrainzClient musicBrainz, PlaylistVideo video, CancellationToken cancellationToken = default)
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

    static string SanitizeFilename(string filename) => filename.Replace('/', '_').Replace('\\', '_').Replace('"', '\'');

    async Task DownloadVideoJob(YoutubeClient youtube, MusicBrainzClient musicBrainz, PlaylistVideo video, CancellationToken cancellationToken = default)
    {
        (string artist, string title) = YouTube.NormalizeMetadata(video);

        string filename = Path.Combine(OutputPath, $"{SanitizeFilename(artist)} - {SanitizeFilename(title)}.mp3");

        await DownloadAudioData(filename, $"https://www.youtube.com/watch?v={video.Id}", cancellationToken);

        TagLib.File file = TagLib.File.Create(filename);

        await YouTube.FetchMetadata(file, video, cancellationToken);
        await MusicBrainz.FetchMetadata(file, musicBrainz, cancellationToken);

        file.RemoveTags(file.TagTypes & ~file.TagTypesOnDisk);
        file.Save();
    }

    static async Task DownloadAudioData(string filename, string url, CancellationToken cancellationToken = default)
    {
        using (Process process = Process.Start(new ProcessStartInfo()
        {
            FileName = "yt-dlp",
            Arguments = $"-o \"{filename}\" -x --audio-format mp3 {url}",
            UseShellExecute = true,
        })!)
        {
            process.WaitForExit();
            if (process.ExitCode != 0) throw new YtdlpExceptionException(process.ExitCode);
            Console.WriteLine("OK");
            return;
        }
    }
}