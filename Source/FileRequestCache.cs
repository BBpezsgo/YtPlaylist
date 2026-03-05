using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hqub.MusicBrainz.Cache;

namespace YtPlaylist;

public class FileRequestCache(string path) : IRequestCache
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(24.0);

    readonly string Path = System.IO.Path.GetFullPath(path);

    Task<bool> IRequestCache.Contains(string request) => Task.FromResult(Contains(request));
    Task<bool> IRequestCache.TryGetCachedItem(string request, out Stream? stream, out HttpStatusCode status) => Task.FromResult(TryGetCachedItem(request, out stream, out status));

    public async Task Add(string request, Stream response, HttpStatusCode status)
    {
        if (!Directory.Exists(Path))
        {
            Directory.CreateDirectory(Path);
        }

        await CacheEntry.Write(Path, request, response, status);
    }

    public bool Contains(string request)
    {
        if (!TryGetCachedItem(request, out Stream? stream, out _)) return false;
        stream.Dispose();
        return true;
    }

    public bool TryGetCachedItem(string request, [NotNullWhen(true)] out Stream? stream, out HttpStatusCode status)
    {
        CacheEntry? item = CacheEntry.Read(Path, request);

        stream = null;
        status = default;

        if (item == null)
        {
            return false;
        }

        if ((DateTime.Now - item.TimeStamp) > Timeout)
        {
            item.Dispose();
            return false;
        }

        stream = item.Stream;
        status = item.Status;
        return stream is not null;
    }

    public int Cleanup()
    {
        int count = 0;

        DateTime now = DateTime.Now;

        foreach (string file in Directory.EnumerateFiles(Path, "*.mb-cache"))
        {
            if ((now - CacheEntry.GetTimestamp(file)) > Timeout)
            {
                File.Delete(file);
            }
        }

        return count;
    }

    public void Clear()
    {
        foreach (string file in Directory.EnumerateFiles(Path, "*.mb-cache"))
        {
            File.Delete(file);
        }
    }

    sealed class CacheEntry : IDisposable
    {
        public Stream? Stream { get; private set; }
        public HttpStatusCode Status { get; private set; }
        public DateTime TimeStamp { get; private set; }
        public string? Request { get; set; }

        public static CacheEntry? Read(string path, string request)
        {
            string filename = GetCacheFileName(path, Encoding.ASCII.GetBytes(request));

            if (!File.Exists(filename)) return null;

            using FileStream stream = File.OpenRead(filename);
            using BinaryReader reader = new(stream, Encoding.UTF8);

            CacheEntry entry = new()
            {
                TimeStamp = TimestampToDateTime(reader.ReadInt64(), DateTimeKind.Utc),
                Status = (HttpStatusCode)reader.ReadInt32(),
                Request = reader.ReadString(),
                Stream = new MemoryStream(reader.ReadBytes(reader.ReadInt32())),
            };

            if (!string.Equals(entry.Request, request, StringComparison.Ordinal)) return null;
            if ((int)entry.Status >= 500) return null;

            return entry;
        }

        public static async Task Write(string path, string request, Stream response, HttpStatusCode status)
        {
            if ((int)status >= 500) return;

            byte[] buffer;
            using (MemoryStream memoryStream = new())
            {
                await response.CopyToAsync(memoryStream);
                buffer = memoryStream.ToArray();
            }

            string filename = GetCacheFileName(path, Encoding.ASCII.GetBytes(request));

            using (StreamReader reader = new(response, leaveOpen: true))
            using (FileStream stream = File.Create(filename))
            using (BinaryWriter writer = new(stream))
            {
                string data = await reader.ReadToEndAsync();
                writer.Write(GetUnixTimestamp());
                writer.Write((int)status);
                writer.Write(request);
                writer.Write(buffer.Length);
                writer.Write(buffer);
            }

            response.Seek(0, SeekOrigin.Begin);
        }

        public static DateTime GetTimestamp(string file)
        {
            long timestamp;

            using (FileStream stream = File.OpenRead(file))
            using (BinaryReader reader = new(stream))
            {
                timestamp = reader.ReadInt64();
            }

            return TimestampToDateTime(timestamp, DateTimeKind.Utc);
        }

        static string GetCacheFileName(string path, ReadOnlySpan<byte> buffer) => System.IO.Path.Combine(path, GetHash(buffer)) + ".bin";

        [SuppressMessage("Security", "CA5351")]
        static string GetHash(ReadOnlySpan<byte> bytes)
        {
            bytes = MD5.HashData(bytes);

            StringBuilder buffer = new();

            for (int i = 0; i < bytes.Length; i += 2)
            {
                buffer.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture).ToLowerInvariant());
            }

            return buffer.ToString();
        }

        static DateTime TimestampToDateTime(long timestamp, DateTimeKind kind) => new DateTime(1970, 1, 1, 0, 0, 0, 0, kind).AddSeconds(timestamp).ToLocalTime();
        static long DateTimeToUtcTimestamp(DateTime dateTime) => (long)(dateTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        static long GetUnixTimestamp() => DateTimeToUtcTimestamp(DateTime.Now);

        public void Dispose() => Stream?.Dispose();
    }
}