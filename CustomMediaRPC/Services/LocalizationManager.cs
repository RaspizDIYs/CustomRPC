using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Threading;

namespace CustomMediaRPC.Services
{
    public static class LocalizationManager
    {
        private static ResourceManager? _resourceManager;

        // Supported languages (Code -> Display Name)
        public static Dictionary<string, string> SupportedLanguages { get; } = new Dictionary<string, string>
        {
            { "en-US", "English" },
            { "ru-RU", "Русский" }
        };

        public static void Initialize(string cultureCode = "ru-RU")
        {
            // Ensure the culture code is supported, fallback to Russian if not or empty
            if (string.IsNullOrEmpty(cultureCode) || !SupportedLanguages.ContainsKey(cultureCode))
            {
                cultureCode = "ru-RU";
            }

            SetCulture(cultureCode);
            _resourceManager = new ResourceManager("CustomMediaRPC.Properties.Resources", Assembly.GetExecutingAssembly());
        }

        public static void SetCulture(string cultureCode)
        {
            try
            {
                var cultureInfo = new CultureInfo(cultureCode);
                Thread.CurrentThread.CurrentUICulture = cultureInfo;
                Thread.CurrentThread.CurrentCulture = cultureInfo; // Optional: Also set CurrentCulture if needed for formatting
            }
            catch (CultureNotFoundException)
            {
                // Fallback if culture is somehow invalid, though we validate against SupportedLanguages
                var fallbackCulture = new CultureInfo("ru-RU");
                Thread.CurrentThread.CurrentUICulture = fallbackCulture;
                Thread.CurrentThread.CurrentCulture = fallbackCulture;
            }
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (_resourceManager == null)
            {
                // Initialize with default if not already done (should be called from App.xaml.cs)
                Initialize(); 
            }
            
            try
            {
                 // Use CurrentUICulture set by SetCulture
                return _resourceManager?.GetString(key, Thread.CurrentThread.CurrentUICulture) ?? defaultValue;
            }
            catch (MissingManifestResourceException)
            {
                return defaultValue; // Or handle the error appropriately
            }
        }
    }
} 