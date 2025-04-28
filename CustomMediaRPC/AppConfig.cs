using System;

namespace CustomMediaRPC
{
    public class AppConfig
    {
        public string? ClientId { get; set; } // Сделал nullable для соответствия проверкам
        public string? LastFmApiKey { get; set; } // Сделал nullable для соответствия проверкам
        public string? LastSelectedSourceAppId { get; set; } // Добавили поле для сохранения ID источника
        public string? PreferredTheme { get; set; } // Добавили поле для темы
        
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(ClientId) && 
                   !string.IsNullOrEmpty(LastFmApiKey) && 
                   LastFmApiKey != "YOUR_LAST_FM_API_KEY_HERE" &&
                   LastFmApiKey.Length >= Constants.LastFm.MIN_API_KEY_LENGTH;
        }
        
        public string? Validate()
        {
            if (string.IsNullOrEmpty(ClientId))
                return "ClientId is not set";
            
            if (string.IsNullOrEmpty(LastFmApiKey))
                return "LastFmApiKey is not set";
            
            if (LastFmApiKey == "YOUR_LAST_FM_API_KEY_HERE")
                return "LastFmApiKey has default value";
            
            if (LastFmApiKey.Length < Constants.LastFm.MIN_API_KEY_LENGTH)
                return $"LastFmApiKey is too short (minimum {Constants.LastFm.MIN_API_KEY_LENGTH} characters)";
            
            return null;
        }
    }
} 