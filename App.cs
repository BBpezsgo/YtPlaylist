using System.Threading.Channels;
using Hqub.MusicBrainz;
using Logger;
using YoutubeExplode;
using YoutubeExplode.Playlists;
using System.Diagnostics;
using YoutubeExplode.Common;

namespace YtPlaylist;

sealed class App
{
    public required AppArguments Arguments { get; init; }

    const int MaxRetries = 1;
    const int MaxConcurrency = 1;
    static readonly TimeSpan CacheTime = TimeSpan.FromDays(500);
    public const string UserAgent = "github.com/BBpezsgo";

    public async Task Run(CancellationToken cancellationToken = default)
    {
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;

        using YoutubeClient youtube = new();

        foreach (string playlistId in Arguments.PlaylistIds)
        {
            HashSet<string> online = [];

            Playlist playlist;
            try
            {
                playlist = await youtube.Playlists.GetAsync($"https://youtube.com/playlist?list={playlistId}", cancellationToken);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) return;
                Log.Error(ex);
                continue;
            }

            Log.Section(
                playlist.Count.HasValue
                ? $"Synchronizing {playlist.Count} videos from playlist {playlist.Title}"
                : $"Synchronizing playlist {playlist.Title}"
            );

            string outputPath = Path.Combine(Arguments.OutputPath, playlist.Title);
            Dictionary<string, string> onDisk = [];

            Log.MajorAction($"Indexing");

            if (!Arguments.UseCache)
            {
                onDisk.Clear();
                IndexFiles(onDisk, outputPath, cancellationToken);
                if (!Arguments.DryRun) WriteIndex(onDisk, outputPath);
            }
            else if (!File.Exists(Path.Combine(outputPath, ".cache")))
            {
                Log.Info($"Index file does not exists {Path.Combine(outputPath, ".cache")}");

                onDisk.Clear();
                IndexFiles(onDisk, outputPath, cancellationToken);
                if (!Arguments.DryRun) WriteIndex(onDisk, outputPath);
            }
            else
            {
                ReadIndex(onDisk, outputPath);
            }


            Log.MajorAction($"Downloading music videos");

            await DownloadPlaylist(playlist, youtube, online, outputPath, onDisk, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            List<(string VideoId, string Filename)> deleteFiles = [];
            foreach ((string videoId, string filename) in onDisk)
            {
                if (!online.Contains(videoId))
                {
                    deleteFiles.Add((videoId, filename));
                }
            }

            if (deleteFiles.Count > 0)
            {
                foreach ((_, string filename) in deleteFiles)
                {
                    Log.Warning($"Music file \"{Path.GetFileNameWithoutExtension(filename)}\" shouldn't be here");
                }

                if (!Arguments.DryRun && Log.AskYesNo("Do you want to delete the files above?", true))
                {
                    foreach ((string videoId, string filename) in deleteFiles)
                    {
                        File.Delete(filename);
                        string lyricsFilename = Path.ChangeExtension(filename, ".lrc");
                        if (File.Exists(lyricsFilename)) File.Delete(lyricsFilename);

                        onDisk.Remove(videoId);
                    }
                }
            }

            if (!Arguments.DryRun) WriteIndex(onDisk, outputPath);

            Log.None($"Videos synchronized");

            if (Arguments.Metadata)
            {
                Log.MajorAction($"Fetching metadata");

                using MusicBrainzClient musicBrainz = new(new HttpClient(new SocketsHttpHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                })
                {
                    DefaultRequestHeaders = { { "User-Agent", UserAgent } },
                    //BaseAddress = new Uri("https://localhost:443/"),
                    BaseAddress = new Uri("https://musicbrainz.org/ws/2/"),
                })
                {
                    Cache = new FileRequestCache(Arguments.HttpCachePath)
                    {
                        Timeout = CacheTime,
                    },
                };

                foreach (string filename in onDisk.Select(v => v.Value).ToArray())
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    using TagLib.File file = TagLib.File.Create(filename);

                    if (string.IsNullOrEmpty(file.Tag.MusicBrainzReleaseId))
                    {
                        string name = Path.GetFileNameWithoutExtension(filename);
                        ReadOnlySpan<string> parts = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        string[]? performers = null;
                        string? title = null;

                        switch (parts.Length)
                        {
                            case 2:
                                performers = parts[0].Split('&', StringSplitOptions.TrimEntries);
                                title = parts[1];
                                break;
                            case > 2:
                                Log.Info($"Invalid filename `{name}`");
                                performers = parts[0].Split('&', StringSplitOptions.TrimEntries);
                                title = string.Join(" - ", parts[1..]);
                                break;
                            default:
                                Log.Warning($"Invalid filename `{name}`");
                                break;
                        }

                        if (performers is not null && !file.Tag.Performers.SequenceEqual(performers))
                        {
                            if (file.Tag.Performers.Length == 0)
                            {
                                Log.None($"Performers added: `{string.Join(" & ", performers)}`");
                            }
                            else
                            {
                                Log.None($"Performers fixed: `{string.Join(" & ", file.Tag.Performers)}` --> `{string.Join(" & ", performers)}`");
                            }
                            file.Tag.Performers = performers;
                        }

                        if (title is not null && file.Tag.Title != title)
                        {
                            if (string.IsNullOrEmpty(file.Tag.Title))
                            {
                                Log.None($"Title added: `{title}`");
                            }
                            else
                            {
                                Log.None($"Title fixed: `{file.Tag.Title}` --> `{title}`");
                            }
                            file.Tag.Title = title;
                        }

                        Log.MinorAction(
                            title is not null
                            ? performers is not null
                            ? $"Fetching metadata for `{string.Join(" & ", performers)} - {title}`"
                            : $"Fetching metadata for `? - {title}`"
                            : $"Fetching metadata for `?`"
                        );
                        await MusicBrainz.FetchMetadata(file, musicBrainz, cancellationToken);

                        if (!Arguments.DryRun) file.Save();
                    }
                }
            }

            if (Arguments.Lyrics)
            {
                Log.MajorAction($"Fetching lyrics");

                using LrcLib lrcLib = new(new(Arguments.HttpCachePath)
                {
                    Timeout = CacheTime,
                });

                foreach (string filename in onDisk.Select(v => v.Value).ToArray())
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (File.Exists(Path.ChangeExtension(filename, ".lrc"))) continue;

                    using TagLib.File file = TagLib.File.Create(filename);

                    if (string.IsNullOrEmpty(file.Tag.Title)
                        || file.Tag.Performers is null
                        || file.Tag.Performers.Length == 0)
                    { continue; }

                    try
                    {
                        Log.MinorAction($"Fetching lyrics for `{file.Tag.FirstPerformer} - {file.Tag.Title}`");
                        LrcLib.LyricsResponse? lyrics = await lrcLib.FetchLyrics(file.Tag.FirstPerformer, file.Tag.Title, null, null, cancellationToken);
                        if (lyrics is null) continue;
                        if (lyrics.SyncedLyrics is null && lyrics.PlainLyrics is null) continue;

                        TagLib.Id3v2.SynchedText[]? synchedTexts = null;
                        string? unsyncedText = null;

                        if (lyrics.SyncedLyrics is not null)
                        {
                            string[] lines = lyrics.SyncedLyrics.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            synchedTexts = new TagLib.Id3v2.SynchedText[lines.Length];
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i];

                                if (!line.StartsWith('['))
                                {
                                    Log.Error($"Invalid lyrics");
                                    goto skipFile;
                                }

                                int j = line.IndexOf(']');

                                if (j == -1)
                                {
                                    Log.Error($"Invalid lyrics");
                                    goto skipFile;
                                }

                                string time = line[1..j];
                                line = line[(j + 1)..].TrimStart();

                                string[] timeSegments = time.Split(':');
                                if (timeSegments.Length != 2)
                                {
                                    Log.Error($"Invalid lyrics");
                                    goto skipFile;
                                }

                                if (!int.TryParse(timeSegments[0], out int minute))
                                {
                                    Log.Error($"Invalid lyrics");
                                    goto skipFile;
                                }

                                if (!double.TryParse(timeSegments[1], out double second))
                                {
                                    Log.Error($"Invalid lyrics");
                                    goto skipFile;
                                }

                                synchedTexts[i] = new TagLib.Id3v2.SynchedText(
                                    (long)TimeSpan.FromSeconds(second + (minute * 60d)).TotalMilliseconds,
                                    line
                                );
                            }
                        }

                        if (lyrics.PlainLyrics is not null)
                        {
                            unsyncedText = lyrics.PlainLyrics;
                        }

                        if (unsyncedText is null && synchedTexts is not null)
                        {
                            unsyncedText = string.Join('\n', synchedTexts.Select(v => v.Text));
                        }

                        Log.None($"Lyrics added ({lyrics.ArtistName} - {lyrics.TrackName} [{lyrics.AlbumName}] {TimeSpan.FromSeconds(lyrics.Duration)})");

                        TagLib.Id3v2.Tag tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);

                        if (synchedTexts is not null)
                        {
                            TagLib.Id3v2.SynchronisedLyricsFrame synchronisedLyricsFrame = new("LRCLib", "eng", TagLib.Id3v2.SynchedTextType.Lyrics)
                            {
                                Text = synchedTexts,
                                Format = TagLib.Id3v2.TimestampFormat.AbsoluteMilliseconds,
                            };
                            tag.ReplaceFrame(TagLib.Id3v2.SynchronisedLyricsFrame.Get(tag, synchronisedLyricsFrame.Description, synchronisedLyricsFrame.Language, synchronisedLyricsFrame.Type, true), synchronisedLyricsFrame);
                        }

                        if (unsyncedText is not null)
                        {
                            TagLib.Id3v2.UnsynchronisedLyricsFrame unsynchronisedLyricsFrame = new("LRCLib", "eng")
                            {
                                Text = unsyncedText,
                            };
                            tag.ReplaceFrame(TagLib.Id3v2.UnsynchronisedLyricsFrame.Get(tag, unsynchronisedLyricsFrame.Description, unsynchronisedLyricsFrame.Language, true), unsynchronisedLyricsFrame);
                        }

                        File.WriteAllText(Path.ChangeExtension(filename, ".lrc"), lyrics.SyncedLyrics ?? unsyncedText);
                    }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        Log.Error(ex);
                        continue;
                    }

                    if (!Arguments.DryRun) file.Save();

                skipFile:;
                }
            }
        }
    }

    #region Index

    static void ReadIndex(Dictionary<string, string> result, string path)
    {
        Log.MinorAction("Reading index from file");

        using FileStream file = new(Path.Combine(path, ".cache"), FileMode.Open, FileAccess.Read);
        using BinaryReader reader = new(file);

        int n = reader.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            string videoId = reader.ReadString();
            string relativeFilename = reader.ReadString();
            result.Add(videoId, Path.GetFullPath(relativeFilename, path));
        }
    }

    static void WriteIndex(Dictionary<string, string> onDisk, string path)
    {
        Log.MinorAction("Writing index to file");

        using FileStream file = new(Path.Combine(path, ".cache"), FileMode.OpenOrCreate, FileAccess.Write);
        using BinaryWriter writer = new(file);

        writer.Write(onDisk.Count);
        foreach ((string videoId, string filename) in onDisk)
        {
            writer.Write(videoId);
            writer.Write(Path.GetRelativePath(path, filename));
        }
    }

    static void IndexFiles(Dictionary<string, string> onDisk, string path, CancellationToken cancellationToken = default)
    {
        Log.MinorAction("Indexing files");
        bool? removeDuplicated = null;

        foreach (string filename in Directory.GetFiles(path, "*.mp3"))
        {
            if (cancellationToken.IsCancellationRequested) return;

            TagLib.File file = TagLib.File.Create(filename, TagLib.ReadStyle.PictureLazy);

            if (!string.IsNullOrWhiteSpace(file.Tag.Description) && file.Tag.Description.Length < 13)
            {
                if (!onDisk.TryAdd(file.Tag.Description, filename))
                {
                    Log.Warning($"Duplicated file: {Path.GetFileName(filename)} and {Path.GetFileName(onDisk[file.Tag.Description])}");

                    if (!removeDuplicated.HasValue)
                    {
                        removeDuplicated = Log.AskYesNo("Do you want to remove duplicated files?", true);
                    }

                    if (removeDuplicated.Value)
                    {
                        File.Delete(filename);
                    }
                }
            }
            else
            {
                Log.Warning($"Unexpected file {Path.GetFileName(filename)}");
            }
        }
    }

    #endregion

    #region YouTube

    Task DownloadPlaylist(Playlist playlist, YoutubeClient youtube, HashSet<string> online, string path, Dictionary<string, string> onDisk, CancellationToken cancellationToken = default)
    {
        Channel<PlaylistVideo> channel = Channel.CreateUnbounded<PlaylistVideo>();

        Span<Task> tasks = new Task[1 + MaxConcurrency];

        tasks[0] = Task.Run(async () =>
        {
            Log.MinorAction("Fetching videos");
            await foreach (Batch<PlaylistVideo> batch in youtube.Playlists.GetVideoBatchesAsync(playlist.Url, cancellationToken))
            {
                foreach (PlaylistVideo video in batch.Items)
                {
                    online.Add(video.Id);
                    if (onDisk.ContainsKey(video.Id)) continue;
                    await channel.Writer.WriteAsync(video, cancellationToken);
                }
                //Log.Debug("Fetching next batch");
            }
            channel.Writer.Complete();
            if (cancellationToken.IsCancellationRequested) return;
            Log.MinorAction("All videos fetched");
        }, cancellationToken);

        for (int i = 0; i < MaxConcurrency; i++)
        {
            tasks[i + 1] = DownloadPlaylistJob(youtube, channel, path, onDisk, cancellationToken);
        }

        return Task.WhenAll(tasks);
    }

    async Task DownloadPlaylistJob(YoutubeClient youtube, Channel<PlaylistVideo> channel, string path, Dictionary<string, string> onDisk, CancellationToken cancellationToken = default)
    {
        await foreach (PlaylistVideo video in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            await HandleVideo(youtube, video, path, onDisk, cancellationToken);
        }
    }

    async Task HandleVideo(YoutubeClient youtube, PlaylistVideo video, string path, Dictionary<string, string> onDisk, CancellationToken cancellationToken = default)
    {
        string artist = video.Author.ChannelTitle;
        string title = video.Title;

        if (title.StartsWith($"{artist} - ", StringComparison.InvariantCultureIgnoreCase))
        {
            title = title[(artist.Length + 3)..].TrimStart();
        }

        artist = artist.TrimEnd(" - Topic").TrimEnd();
        string[] artists = artist.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (onDisk.TryGetValue(video.Id, out string? filename))
        {
            //Log.Debug($"File \"{Path.GetFileName(filename)}\" already exists, skipping (indexed)");
            return;
        }
        else
        {
            filename = Path.Combine(path, $"{SanitizeFilename(artist)} - {SanitizeFilename(title)}.mp3");

            if (Arguments.Download)
            {
                if (File.Exists(filename))
                {
                    Log.Debug($"File \"{Path.GetFileName(filename)}\" already exists, skipping download");
                }
                else
                {
                    if (Arguments.DryRun)
                    {
                        Log.MinorAction($"Should download \e[1m{video.Title}\e[22m");
                    }
                    else
                    {
                        Log.MinorAction($"Downloading \e[1m{video.Title}\e[22m");

                        Exception? downloadException = await RunRetries(
                            (cancellationToken) => Task.Run(() => YtDlp.DownloadAudioData(filename, $"https://www.youtube.com/watch?v={video.Id}"), cancellationToken),
                            GenericHttpRetryFilter,
                            MaxRetries,
                            cancellationToken
                        );
                        switch (downloadException)
                        {
                            case HttpRequestException v:
                                Log.Error($"Failed to download \e[1m{video.Title}\e[22m: HTTP {(int)v.StatusCode!} ({v.StatusCode})");
                                return;
                            case not null:
                                Log.Error(downloadException);
                                return;
                        }
                    }
                }
            }
            else if (!File.Exists(filename))
            {
                return;
            }

            onDisk[video.Id] = filename;
        }

        if (!Arguments.DryRun)
        {
            using TagLib.File file = TagLib.File.Create(filename);
            bool changed = false;

            if (file.Tag.Description != video.Id.Value)
            {
                file.Tag.Description = video.Id.Value;
                changed = true;
            }

            if (Arguments.Metadata)
            {
                if (file.Tag.Pictures.Length == 0
                    && await TagUtils.DownloadCoverImage(file, new Uri(video.Thumbnails.OrderByDescending(v => v.Resolution.Area).First().Url, UriKind.Absolute), "YouTube", TagLib.PictureType.FrontCover, cancellationToken))
                {
                    changed = true;
                }

                if (file.Tag.Title != title)
                {
                    file.Tag.Title = title;
                    changed = true;
                }

                if (file.Tag.Performers is null
                    || !file.Tag.Performers.SequenceEqual(artists))
                {
                    file.Tag.Performers = artists;
                    changed = true;
                }
            }

            if (changed) file.Save();
        }
    }

    static async Task<bool> GenericHttpRetryFilter(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is HttpRequestException httpRequestException
            && httpRequestException.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            await Task.Delay(1000, cancellationToken);
            return true;
        }
        return false;
    }

    static async Task<Exception?> RunRetries(Func<CancellationToken, Task> callback, Func<Exception, CancellationToken, Task<bool>> exceptionHandler, int retries, CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (int retry = 1; retry <= retries; retry++)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            try
            {
                await callback.Invoke(cancellationToken);
                return null;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) return null;
                if (!await exceptionHandler.Invoke(ex, cancellationToken)) return ex;
                lastException = ex;
                continue;
            }
        }

        if (cancellationToken.IsCancellationRequested) return null;
        return lastException;
    }

    static string SanitizeFilename(string filename)
    {
        char[] result = filename.ToCharArray();
        for (int i = 0; i < result.Length; i++)
        {
            ref char c = ref result[i];
            c = c switch
            {
                '/' or '\\' or '"' or '\'' => '_',
                '\n' or '\r' or '\t' => ' ',
                _ => c,
            };
            if (!char.IsAscii(c)
                && char.GetUnicodeCategory(c)
                    is not System.Globalization.UnicodeCategory.LowercaseLetter
                    and not System.Globalization.UnicodeCategory.UppercaseLetter
                    and not System.Globalization.UnicodeCategory.TitlecaseLetter
                    and not System.Globalization.UnicodeCategory.ModifierLetter
                    and not System.Globalization.UnicodeCategory.OtherLetter
                    and not System.Globalization.UnicodeCategory.LetterNumber)
            {
                c = '?';
            }
        }
        return new string(result);
    }

    #endregion
}