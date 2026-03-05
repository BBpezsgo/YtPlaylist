using System.Diagnostics;
using YtPlaylist;

static class YtDlp
{
    public static void DownloadAudioData(string filename, string url)
    {
        using Process process = Process.Start(new ProcessStartInfo()
        {
            FileName = "yt-dlp",
            Arguments = $"-o \"{filename}\" -x --audio-format mp3 {url}",
            UseShellExecute = true,
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0) throw new YtDlpExceptionException(process.ExitCode);
    }
}
