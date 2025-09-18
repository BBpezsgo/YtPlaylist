using Logger;

static class Utils
{
    public static async Task<bool> DownloadCoverImage(TagLib.File file, Uri url, string description, TagLib.PictureType type, CancellationToken cancellationToken)
    {
        byte[]? imageBytes = null;
        using (HttpClient client = new())
        {
            try
            {
                imageBytes = await client.GetByteArrayAsync(url, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        Log.None("Cover art downloaded");
        TagLib.Id3v2.AttachmentFrame cover = new()
        {
            Type = type,
            Description = description,
            MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg,
            Data = imageBytes,
            TextEncoding = TagLib.StringType.UTF16,
        };
        file.Tag.Pictures = [cover];
        return true;
    }
}