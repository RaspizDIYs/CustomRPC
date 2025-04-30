using System;
using System.Linq;
using DiscordRPC;
using CustomMediaRPC.Utils;
using Windows.Storage.Streams;

namespace CustomMediaRPC.Models;

public enum MediaPlaybackStatus
{
    Unknown,
    Playing,
    Paused,
    Stopped
}

public class MediaState
{
    public TimeSpan? CurrentPosition { get; set; }
    public TimeSpan? TotalDuration { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public MediaPlaybackStatus Status { get; set; }
    public string? SourceAppId { get; set; }
    public string? CoverArtUrl { get; set; }
    public IRandomAccessStreamReference? CoverArtThumbnail { get; set; }
    
    public string GetDisplayTitle()
    {
        if (string.IsNullOrEmpty(Artist) || Artist == Constants.Media.UNKNOWN_ARTIST)
            return Title ?? Constants.Media.UNKNOWN_TITLE;
            
        return $"{Artist} — {Title ?? Constants.Media.UNKNOWN_TITLE}";
    }
    
    public string GetStatusEmoji()
    {
        return Status switch
        {
            MediaPlaybackStatus.Playing => Constants.Discord.PLAYING_EMOJI,
            MediaPlaybackStatus.Paused => Constants.Discord.PAUSED_EMOJI,
            MediaPlaybackStatus.Stopped => Constants.Discord.STOPPED_EMOJI,
            _ => string.Empty
        };
    }
    
    public string GetStatusText()
    {
        return Status switch
        {
            MediaPlaybackStatus.Playing => "Playing",
            MediaPlaybackStatus.Paused => "Paused",
            MediaPlaybackStatus.Stopped => "Stopped",
            _ => "Unknown"
        };
    }

    public string GetSourceName()
    {
        if (string.IsNullOrEmpty(SourceAppId))
            return Constants.Media.UNKNOWN_SOURCE;

        return SourceAppId.Split('!').FirstOrDefault() ?? Constants.Media.UNKNOWN_SOURCE;
    }

    public bool IsPlaying => Status == MediaPlaybackStatus.Playing;
    public bool IsPaused => Status == MediaPlaybackStatus.Paused;
    public bool IsStopped => Status == MediaPlaybackStatus.Stopped;
    public bool IsUnknown => Status == MediaPlaybackStatus.Unknown;

    public override string ToString()
    {
        return $"MediaState[Source={GetSourceName()}, Title='{Title}', Artist='{Artist}', Album='{Album}', Status={Status}]";
    }
} 