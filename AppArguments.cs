using System.Collections.Immutable;

namespace YtPlaylist;

sealed class AppArguments
{
    public required ImmutableArray<string> PlaylistIds { get; init; }
    public required bool UseCache { get; init; }
    public required string HttpCachePath { get; init; }
    public required bool DryRun { get; init; }
    public required bool Download { get; init; }
    public required bool Metadata { get; init; }
    public required bool Lyrics { get; init; }
    public required string OutputPath { get; init; }
}
