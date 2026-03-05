using System.Diagnostics.CodeAnalysis;
using Hqub.MusicBrainz;
using Hqub.MusicBrainz.Entities;
using Logger;

namespace YtPlaylist;

static class MusicBrainz
{
    static async Task<Artist?> LookupArtist(MusicBrainzClient musicBrainz, string artist, CancellationToken cancellationToken)
    {
        QueryResult<Artist>? onlineArtists = null;

        try
        {
            onlineArtists = await musicBrainz.Artists.SearchAsync(
                $"artist:{artist.Quote()} OR alias:{artist.Quote()}",
                2);
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            Log.Error(ex);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        List<Artist> candidates = GetCandidates(onlineArtists, (a, b) =>
        {
            if (a.Score > b.Score) return -1;
            if (a.Score < b.Score) return +1;

            if (a.Name == artist && b.Name != artist) return -1;
            if (a.Name != artist && b.Name == artist) return +1;

            bool fa = (a.Aliases ?? []).Any(v => string.Equals(v.Name, artist, StringComparison.OrdinalIgnoreCase));
            bool fb = (b.Aliases ?? []).Any(v => string.Equals(v.Name, artist, StringComparison.OrdinalIgnoreCase));
            if (fa && !fb) return -1;
            if (!fa && fb) return +1;

            return 0;
        });

        if (candidates.Count > 1)
        {
            Log.Warning($"Multiple artists found with name \"{artist}\"");
            foreach (Artist item in candidates)
            {
                MusicBrainzUtils.Print(item);
            }

            return null;
        }

        return candidates.FirstOrDefault();
    }

    static async Task<Recording?> LookupRecording(MusicBrainzClient musicBrainz, (string Name, bool IsMBID) artist, string recordingTitle, CancellationToken cancellationToken)
    {
        QueryResult<Recording>? recordings = null;

        try
        {
            recordings = await musicBrainz.Recordings.SearchAsync(
                artist.IsMBID
                ? $"arid:{artist.Name} AND recording:{recordingTitle.Quote()}"
                : $"artistname:{artist.Name.Quote()} AND recording:{recordingTitle.Quote()}",
                2);
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            Log.Error(ex);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (recordings.IsNullOrEmpty())
        {
            return null;
        }

        if (recordings.First().Score > 0)
        {
            List<Recording> bestRecordings = [];
            int bestScore = 0;

            foreach (Recording recording in recordings)
            {
                if (bestRecordings.Count == 0)
                {
                    bestRecordings.Add(recording);
                    bestScore = recording.Score;
                }
                else if (recording.Score > bestScore)
                {
                    bestRecordings.Clear();
                    bestRecordings.Add(recording);
                    bestScore = recording.Score;
                }
                else if (recording.Score == bestScore)
                {
                    bestRecordings.Add(recording);
                    bestScore = recording.Score;
                }
            }

            if (bestRecordings.Count > 1)
            {
                Log.Warning($"Multiple recordings found: {Ansi.Bold(artist.Name)} - {Ansi.Bold(recordingTitle)}");
                foreach (Recording recording in bestRecordings) MusicBrainzUtils.Print(recording);
                return null;
            }

            if (bestScore != 100)
            {
                Log.Warning($"Similar recording found: {Ansi.Bold(artist.Name)} - {Ansi.Bold(recordingTitle)} --> {Ansi.Bold(string.Join(" & ", bestRecordings[0].Credits.Select(v => v.Name)))} - {Ansi.Bold(bestRecordings[0].Title)}");
                return null;
            }

            return bestRecordings.First();
        }
        else
        {
            if (recordings.Count > 1)
            {
                Log.Warning($"Multiple recordings found: {Ansi.Bold(artist.Name)} - {Ansi.Bold(recordingTitle)}");
                return null;
            }

            return recordings.FirstOrDefault();
        }
    }

    static async Task<Recording?> LookupRecording(MusicBrainzClient musicBrainz, IEnumerable<string> artists, string recordingTitle, CancellationToken cancellationToken)
    {
        foreach (string artist in artists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Recording? recording = await LookupRecording(musicBrainz, (artist, false), recordingTitle, cancellationToken);

            if (recording is null)
            {
                Artist? artist_ = await LookupArtist(musicBrainz, artist, cancellationToken);

                if (artist_ is not null)
                {
                    recording = await LookupRecording(musicBrainz, (artist_.Id, true), recordingTitle, cancellationToken);
                }
            }

            if (recording is null)
            {
                continue;
            }

            return recording;
        }

        return null;
    }

    static List<T> GetCandidates<T>(IEnumerable<T>? items, Comparison<T> comparison)
    {
        if (items is null || !items.Any()) return [];

        T? best = default;
        foreach (T item in items)
        {
            if (best is null || comparison(best, item) > 0)
            {
                best = item;
            }
        }

        List<T> candidates = [];
        foreach (T item in items)
        {
            if (comparison(best!, item) == 0)
            {
                candidates.Add(item);
            }
        }
        return candidates;
    }

    static int CompareReleases(Release a, Release b)
    {
        if (a.Status == "Official" && b.Status != "Official") return -1;
        if (a.Status != "Official" && b.Status == "Official") return +1;

        return 0;
    }

    static Release? GetRelease(Recording? recording)
    {
        if (recording is null) return null;
        if (recording.Releases.IsNullOrEmpty()) return null;

        List<Release> candidates = GetCandidates(recording.Releases, CompareReleases);

        if (candidates.Count > 1)
        {
            Log.Warning($"Multiple releases found for song `{string.Join(" & ", (recording.Credits ?? []).Select(v => v.Name))} - {recording.Title}`");
            foreach (Release item in candidates)
            {
                MusicBrainzUtils.Print(item);
            }

            return null;
        }

        return candidates.FirstOrDefault();
    }

    [return: NotNullIfNotNull(nameof(v))]
    static string? FixMetaString(string? v)
    {
        if (v is null) return null;
        int i = v.IndexOf('(');
        if (i != -1)
        {
            return v[..i].TrimEnd();
        }
        return v;
    }

    public static async Task FetchMetadata(TagLib.File file, MusicBrainzClient musicBrainz, CancellationToken cancellationToken)
    {
        string? title = FixMetaString(file.Tag.Title);
        string[]? artists = [.. (file.Tag.Performers ?? []).Select(FixMetaString)!];

        if (string.IsNullOrEmpty(title) || artists.Length == 0)
        {
            Log.Warning($"Empty file metadata");
            return;
        }

        Recording? recording = await LookupRecording(musicBrainz, artists.Select(FixMetaString)!, title, cancellationToken);

        if (recording is null)
        {
            Log.Warning($"No recording found for `{string.Join(" & ", artists)} - {title}`");
            return;
        }

        //MusicBrainzUtils.Print(recording);
        //Console.ReadKey();

        if (file.Tag.Title != recording.Title)
        {
            Log.Warning($"Title must be fixed: `{file.Tag.Title}` --> `{recording.Title}`");
            //file.Tag.Title = release.Title;
        }

        if (recording.Credits is not null)
        {
            string[] performers = [.. recording.Credits.Select(v => v.Artist.Name)];
            if (!(file.Tag.Performers ?? []).SequenceEqual(performers))
            {
                Log.Warning($"Performers must be fixed: `{string.Join(" & ", file.Tag.Performers ?? [])}` --> `{string.Join(" & ", performers)}`");
                //file.Tag.Performers = performers;
            }
        }

        var _release = GetRelease(recording);

        if (recording.Releases is null)
        {
            Log.Warning($"No release found: {Ansi.Bold(string.Join(" & ", artists))} - {Ansi.Bold(title)}");
        }
        else if (recording.Releases.Count != 1)
        {
            Log.Warning($"Multiple releases found: {Ansi.Bold(string.Join(" & ", artists))} - {Ansi.Bold(title)}");
            MusicBrainzUtils.Print(recording);
        }
        else
        {
            Release release = recording.Releases[0];

            if (file.Tag.Pictures.Length == 0 || file.Tag.Pictures[0].Description != "MusicBrainz")
            {
                await TagUtils.DownloadCoverImage(file, CoverArtArchive.GetCoverArtUri(release.Id), "MusicBrainz", TagLib.PictureType.FrontCover, cancellationToken);
            }

            file.Tag.MusicBrainzReleaseStatus = release.Status;
            file.Tag.MusicBrainzReleaseCountry = release.Country;
            file.Tag.MusicBrainzReleaseId = release.Id;

            if (release.Date is not null)
            {
                string[] v = release.Date.Split('-');
                if (v.Length >= 1 && uint.TryParse(v[0], out uint year))
                {
                    if (file.Tag.Year != year)
                    {
                        if (file.Tag.Year == default)
                        {
                            Log.None($"Year added: `{year}`");
                        }
                        else
                        {
                            Log.None($"Year fixed: `{file.Tag.Year}` --> `{year}`");
                        }
                        file.Tag.Year = year;
                    }
                }
            }

            if (release.Genres is not null)
            {
                string[] genres = [.. release.Genres.Select(v => v.Name)];
                if (!(file.Tag.Genres ?? []).SequenceEqual(genres))
                {
                    if (file.Tag.Genres is null || file.Tag.Genres.Length == 0)
                    {
                        Log.None($"Genres added: `{string.Join(", ", genres)}`");
                    }
                    else
                    {
                        Log.None($"Genres fixed: `{string.Join(", ", file.Tag.Genres)}` --> `{string.Join(", ", genres)}`");
                    }
                    file.Tag.Genres = genres;
                }
            }

            if (release.Media is not null && release.Media.Count == 1)
            {
                Medium media = release.Media[0];

                //if (media.TrackCount > 1 && media.Position > 0 && media.Position <= media.TrackCount)
                //{
                //    if (file.Tag.TrackCount != media.TrackCount)
                //    {
                //        Log.None($"Track count fixed: `{file.Tag.TrackCount}` --> `{media.TrackCount}`");
                //        file.Tag.TrackCount = (uint)media.TrackCount;
                //    }
                //
                //    if (file.Tag.Track != media.Position)
                //    {
                //        Log.None($"Track fixed: `{file.Tag.Track}` --> `{media.Position}`");
                //        file.Tag.Track = (uint)media.Position;
                //    }
                //}
            }

            if (release.ReleaseGroup is not null)
            {
                ReleaseGroup releaseGroup = release.ReleaseGroup;

                file.Tag.MusicBrainzReleaseGroupId = releaseGroup.Id;

                if (file.Tag.Album != releaseGroup.Title)
                {
                    if (string.IsNullOrEmpty(file.Tag.Album))
                    {
                        Log.None($"Album added: `{releaseGroup.Title}`");
                    }
                    else
                    {
                        Log.None($"Album fixed: `{file.Tag.Album}` --> `{releaseGroup.Title}`");
                    }
                    file.Tag.Album = releaseGroup.Title;
                }

                if (releaseGroup.Credits is not null)
                {
                    string[] albumArtists = [.. releaseGroup.Credits.Select(v => v.Artist.SortName)];
                    if (!(file.Tag.AlbumArtists ?? []).SequenceEqual(albumArtists))
                    {
                        if (file.Tag.AlbumArtists is null || file.Tag.AlbumArtists.Length == 0)
                        {
                            Log.None($"Album artists added: `{string.Join(" & ", albumArtists)}`");
                        }
                        else
                        {
                            Log.None($"Album artists fixed: `{string.Join(" & ", file.Tag.AlbumArtists ?? [])}` --> `{string.Join(" & ", albumArtists)}`");
                        }
                        file.Tag.AlbumArtists = albumArtists;
                    }
                }
            }
        }
    }
}
