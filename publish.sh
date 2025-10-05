#!/bin/sh
dotnet publish ./YtPlaylist.csproj -c Release -o ./publish/linux-x64 -r linux-x64 /p:UseAppHost=true
dotnet publish ./YtPlaylist.csproj -c Release -o ./publish/win-x64 -r win-x64 /p:UseAppHost=true /p:DefineConstants=WIN

mv ./publish/linux-x64/YtPlaylist ./publish/YtPlaylist-linux-x64
rm -r ./publish/linux-x64

mv ./publish/win-x64/YtPlaylist.exe ./publish/YtPlaylist-win-x64.exe
rm -r ./publish/win-x64
