using YoutubeExplode.Playlists;

namespace YtPlaylist;

static class YouTube
{
    public static async Task FetchMetadata(TagLib.File file, PlaylistVideo video, CancellationToken cancellationToken)
    {
        (string artist, string title) = NormalizeMetadata(video);

        if (file.Tag.Pictures.Length == 0)
        {
            await Utils.DownloadCoverImage(file, new Uri(video.Thumbnails.OrderByDescending(v => v.Resolution.Area).First().Url, UriKind.Absolute), "YouTube", TagLib.PictureType.FrontCover, cancellationToken);
        }

        string[] artists = artist.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        file.Tag.Title = title;
        file.Tag.Performers = artists;
        file.Tag.Description = video.Id;
    }

    public static (string Author, string Title) NormalizeMetadata(PlaylistVideo video)
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
