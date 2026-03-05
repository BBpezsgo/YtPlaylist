using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using YtPlaylist;

sealed class LrcLib : IDisposable
{
    readonly HttpClient Client;
    readonly FileRequestCache? Cache;
    readonly TimeSpan Cooldown;
    DateTime LastRequest;

    public LrcLib(FileRequestCache cache)
    {
        Client = new HttpClient(new SocketsHttpHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            DefaultRequestHeaders = { { "User-Agent", App.UserAgent } },
            BaseAddress = new Uri("https://lrclib.net/"),
        };
        Cache = cache;
        Cooldown = TimeSpan.FromSeconds(5);
    }

    public sealed class LyricsResponse
    {
        [JsonPropertyName("id")] public required int Id { get; set; }
        [JsonPropertyName("trackName")] public required string TrackName { get; set; }
        [JsonPropertyName("artistName")] public required string ArtistName { get; set; }
        [JsonPropertyName("albumName")] public required string AlbumName { get; set; }
        [JsonPropertyName("duration")] public required double Duration { get; set; }
        [JsonPropertyName("instrumental")] public required bool Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")] public required string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public required string? SyncedLyrics { get; set; }
    }

    async Task Delay(CancellationToken cancellationToken)
    {
        TimeSpan timeSinceLastRequest = DateTime.UtcNow - LastRequest;
        if (timeSinceLastRequest < Cooldown)
        {
            await Task.Delay(Cooldown - timeSinceLastRequest, cancellationToken);
        }
        LastRequest = DateTime.UtcNow;
    }

    public async Task<LyricsResponse?> FetchLyrics(string artistName, string trackName, string? albumName, int? duration, CancellationToken cancellationToken = default)
    {
        string uri;

        {
            StringBuilder uriBuilder = new();
            uriBuilder.Append("/api/get");

            uriBuilder.Append($"?artist_name={HttpUtility.UrlEncode(artistName)}");
            uriBuilder.Append($"&track_name={HttpUtility.UrlEncode(trackName)}");
            if (albumName is not null) uriBuilder.Append($"&album_name={HttpUtility.UrlEncode(albumName)}");
            if (duration.HasValue) uriBuilder.Append($"&duration={duration.Value.ToString()}");
            uri = uriBuilder.ToString();
        }


        if (Cache is null || !Cache.TryGetCachedItem(uri, out Stream? stream, out HttpStatusCode status))
        {
            await Delay(cancellationToken);

            HttpResponseMessage response = await Client.GetAsync(uri, cancellationToken);
            stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (Cache is not null) await Cache.Add(uri, stream, response.StatusCode);

            status = response.StatusCode;
        }

        if (status == HttpStatusCode.NotFound) return null;

        if (!((int)status >= 200 && (int)status <= 299))
        {
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)status}",
                null,
                status
            );
        }

        using StreamReader reader = new(stream);
        string text = await reader.ReadToEndAsync(cancellationToken);
        return JsonSerializer.Deserialize<LyricsResponse>(text);
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}