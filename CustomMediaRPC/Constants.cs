namespace CustomMediaRPC;

public static class Constants
{
    public static class Media
    {
        public const int MAX_DISPLAY_LENGTH = 50;
        public const int MAX_PRESENCE_TEXT_LENGTH = 128;
        public const string DEFAULT_IMAGE_URL = "https://i.pinimg.com/736x/40/4c/b3/404cb3a9d233711daea21db39905fd3f.jpg";
        public const string UNKNOWN_TITLE = "Unknown Title";
        public const string UNKNOWN_ARTIST = "Unknown Artist";
        public const string UNKNOWN_SOURCE = "Unknown Source";
    }

    public static class LastFm
    {
        public const string API_BASE_URL = "https://ws.audioscrobbler.com/2.0/";
        public const string USER_AGENT = "CustomMediaRPC/1.0";
        public const int MIN_API_KEY_LENGTH = 32;
        public const int MAX_REQUESTS_PER_MINUTE = 5;
        public const int CACHE_DURATION_MINUTES = 60;
    }

    public static class Discord
    {
        public const string PLAYING_EMOJI = "▶️";
        public const string PAUSED_EMOJI = "⏸️";
        public const string STOPPED_EMOJI = "⏹️";
    }

    public static class Timeouts
    {
        public static readonly TimeSpan HTTP_TIMEOUT = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan PRESENCE_UPDATE_THROTTLE = TimeSpan.FromMilliseconds(1500);
    }
} 