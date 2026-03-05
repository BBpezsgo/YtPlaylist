namespace YtPlaylist;

public class YtDlpExceptionException(int exitCode) : Exception($"yt-dlp exited with code {exitCode}")
{
    public int ExitCode { get; } = exitCode;
}
