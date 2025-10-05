# YouTube Playlist Downloader

[![.Net 10.0](https://img.shields.io/badge/10.0-606060?style=flat-square&logo=dotnet&labelColor=512BD4)](#)

Downloads and synchronizes YouTube playlists as MP3 files so you can listen to them offline!!!

## Example Usage:

`YtPlaylist -i YOUTUBE_PLAYLIST_ID -o ./Music`

> [!TIP]
> You can get the playlist id by navigating to the playlist on YouTube. For example:
> 
> https://music.youtube.com/playlist?list=PLCXNT9D5QsgZZrogN4KV__ImVNQWTmRjs
> 
> And copy the part after the "list=" parameter, in this example its "PLCXNT9D5QsgZZrogN4KV__ImVNQWTmRjs".

This will also fill the MP3 files' tags with cover art, album, artist and title from MusicBrainz.
Those things are extracted from the YouTube video's title and channel name, so it may be inaccurate.
If it can't find the music on MusicBrainz, it will use the thumbnail as the cover art, the channel name as the artist and the video title as the song title.
If the title contains the channel's name, it will remove it. It will also remove the "Topic" suffix from the channel name.

If you give an existing directory as the output, or run the command twice, it will only download the music that aren't present in the directory but present in the playlist, and ask you if you want to delete the music files that are not present in the playlist anymore.
You can also rename the files, because the youtube video's id is stored in the MP3 file, so no worries.

## Would you use this?

This program fulfills my needs, so no additional crazy features are planned, but if you want some you would actually use, tell me!
