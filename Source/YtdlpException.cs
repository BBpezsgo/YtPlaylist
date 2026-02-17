namespace YtPlaylist;

public class YtdlpExceptionException(int exitCode) : Exception($"yt-dlp exited with code {exitCode}")
{
    public int ExitCode { get; } = exitCode;
}
