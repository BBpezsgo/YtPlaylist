using Hqub.MusicBrainz;
using Hqub.MusicBrainz.Entities;
using Hqub.MusicBrainz.Entities.Collections;
using Logger;

static class MusicBrainz
{
    static DateTimeOffset _lastMusicBrainzRequest;
    const double _musicBrainzRate = 1;
    static async Task WaitMusicBrainzRateLimit()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_lastMusicBrainzRequest != default && (now - _lastMusicBrainzRequest).TotalSeconds < _musicBrainzRate)
        {
            await Task.Delay((int)((_musicBrainzRate - (now - _lastMusicBrainzRequest).TotalSeconds) * 1000));
        }

        _lastMusicBrainzRequest = now;
    }

    static List<T> GetCandidates<T>(IEnumerable<T>? items, Comparison<T> comparison)
    {
        T? onlineArtist = default;

        if (items is null || !items.Any())
        {
            return [];
        }

        foreach (T item in items)
        {
            if (onlineArtist is null || comparison(onlineArtist, item) > 0)
            {
                onlineArtist = item;
            }
        }

        List<T> candidates = [];

        foreach (T item in items)
        {
            if (comparison(onlineArtist!, item) == 0)
            {
                candidates.Add(item);
            }
        }
        return candidates;
    }

    static async Task<Artist?> LookupArtist(MusicBrainzClient musicBrainz, string artist, CancellationToken cancellationToken)
    {
        await WaitMusicBrainzRateLimit();
        ArtistList? onlineArtists = null;
        WebServiceException? exception = null;
        for (int retry = 0; retry < 4; retry++)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            try
            {
                onlineArtists = await musicBrainz.Artists.SearchAsync(
                    $"artist:{artist.Quote()} OR alias:{artist.Quote()}",
                    2);
                break;
            }
            catch (WebServiceException ex)
            {
                exception = ex;
                await Task.Delay(TimeSpan.FromSeconds(4d), cancellationToken);
            }
        }

        if (cancellationToken.IsCancellationRequested) return null;

        if (exception is not null)
        {
            Log.Error(exception.Message);
            return null;
        }

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
        await WaitMusicBrainzRateLimit();
        RecordingList? recordings = null;
        WebServiceException? exception = null;
        for (int retry = 0; retry < 4; retry++)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            try
            {
                recordings = await musicBrainz.Recordings.SearchAsync(
                    artist.IsMBID
                    ? $"arid:{artist.Name} AND recording:{recordingTitle.Quote()}"
                    : $"artistname:{artist.Name.Quote()} AND recording:{recordingTitle.Quote()}",
                    2);
                break;
            }
            catch (WebServiceException ex)
            {
                exception = ex;
                await Task.Delay(TimeSpan.FromSeconds(4d), cancellationToken);
            }
        }

        if (cancellationToken.IsCancellationRequested) return null;

        if (exception is not null)
        {
            Log.Error(exception.Message);
            return null;
        }

        if (recordings.IsNullOrEmpty())
        {
            return null;
        }

        // The database is crappy
        //if (recordings.Count == 2 && recordings.Items[0].Score == recordings.Items[1].Score)
        //{
        //    errors.Add($"Multiple recordings found by \"{artist}\" with title \"{title}\"");
        //    continue;
        //}

        return recordings.FirstOrDefault();
    }

    static async Task<Recording?> LookupRecording(MusicBrainzClient musicBrainz, IEnumerable<string> artists, string recordingTitle, CancellationToken cancellationToken)
    {
        foreach (string artist in artists)
        {
            if (cancellationToken.IsCancellationRequested) return null;

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

    static Release? GetRelease(Recording? recording)
    {
        if (recording is null) return null;

        if (recording.Releases.IsNullOrEmpty())
        {
            return null;
        }

        List<Release> candidates = GetCandidates(recording.Releases.DistinctBy(v => v.Title), (a, b) =>
        {
            if (a.Status == "Official" && b.Status != "Official") return -1;
            if (a.Status != "Official" && b.Status == "Official") return +1;

            if (a.Media is not null && b.Media is null) return -1;
            if (a.Media is null && b.Media is not null) return +1;

            if (a.Media is not null && b.Media is not null)
            {
                if (a.Media.Count > b.Media.Count) return -1;
                if (a.Media.Count < b.Media.Count) return +1;

                if (a.Media.Count == 1 && b.Media.Count == 1)
                {
                    if (a.Media[0].TrackCount > b.Media[0].TrackCount) return -1;
                    if (a.Media[0].TrackCount < b.Media[0].TrackCount) return +1;
                }
            }

            return 0;
        });

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

    static string FixMetaString(string v)
    {
        int i = v.IndexOf('(');
        if (i != -1)
        {
            return v[..i].TrimEnd();
        }
        return v;
    }

    public static async Task<bool> FetchMetadata(TagLib.File file, MusicBrainzClient musicBrainz, CancellationToken cancellationToken)
    {
        string title = FixMetaString(file.Tag.Title);
        IEnumerable<string> artists = file.Tag.Performers.Select(FixMetaString);

        if (cancellationToken.IsCancellationRequested) return false;

        Recording? recording = await LookupRecording(musicBrainz, artists.Select(FixMetaString), title, cancellationToken);

        if (recording is null)
        {
            Log.Warning($"No recording found for `{string.Join(" & ", artists)} - {title}`");
            return false;
        }

        Release? release = GetRelease(recording);

        if (release is not null)
        {
            if (file.Tag.Pictures.Length == 0 || file.Tag.Pictures[0].Description != "MusicBrainz")
            {
                await Utils.DownloadCoverImage(file, CoverArtArchive.GetCoverArtUri(release.Id), "MusicBrainz", TagLib.PictureType.FrontCover, cancellationToken);
            }

            file.Tag.MusicBrainzReleaseStatus = release.Status;
            file.Tag.MusicBrainzReleaseCountry = release.Country;
            file.Tag.MusicBrainzReleaseId = release.Id;

            //if (release.Credits is not null)
            //{
            //    string[] performers = [.. release.Credits.Select(v => v.Artist.Name)];
            //    if (!(file.Tag.Performers ?? []).SequenceEqual(performers))
            //    {
            //        Log.None($"Performers fixed: `{string.Join(" & ", file.Tag.Performers ?? [])}` -> `{string.Join(" & ", performers)}`");
            //        file.Tag.Performers = performers;
            //    }
            //}

            //if (file.Tag.Title != release.Title)
            //{
            //    Log.None($"Title fixed: `{file.Tag.Title}` -> `{release.Title}`");
            //    file.Tag.Title = release.Title;
            //}

            if (release.ReleaseGroup is not null)
            {
                if (file.Tag.Album != release.ReleaseGroup.Title)
                {
                    Log.None($"Album fixed: `{file.Tag.Album}` -> `{release.ReleaseGroup.Title}`");
                    file.Tag.Album = release.ReleaseGroup.Title;
                }

                file.Tag.MusicBrainzReleaseGroupId = release.ReleaseGroup.Id;
                if (release.ReleaseGroup.Credits is not null)
                {
                    string[] albumArtists = [.. release.ReleaseGroup.Credits.Select(v => v.Artist.SortName)];
                    if (!(file.Tag.AlbumArtists ?? []).SequenceEqual(albumArtists))
                    {
                        Log.None($"Album artists fixed: `{string.Join(" & ", file.Tag.AlbumArtists ?? [])}` -> `{string.Join(" & ", albumArtists)}`");
                        file.Tag.AlbumArtists = albumArtists;
                    }
                }
            }
        }

        return true;
    }
}