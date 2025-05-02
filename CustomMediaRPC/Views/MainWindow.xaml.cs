using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Control;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using CustomMediaRPC.Utils;
using CustomMediaRPC.Models;
using System.Windows;
using System.Windows.Controls;
using DiscordRPC;
using DiscordRPC.Logging;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Documents;
using Wpf.Ui.Controls;
using System.Text; // Добавим для Base64
using System.ComponentModel; // Добавлено для Closing
using System.Windows.Data; // Добавлено для конвертера
using System.Globalization; // Добавлено для конвертера
using System.Reflection; // Для получения версии
using CustomMediaRPC.Views;
using CustomMediaRPC.Services; // Возвращаем обратно
using Windows.Storage.Streams; // Добавляем using
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using CustomMediaRPC.Properties; // Add this for direct access to resources

namespace CustomMediaRPC.Views;

// Add constants for internal site names
public static class LinkButtonKeys
{
    public const string GitHub = "GitHub";
    public const string Genius = "Genius";
    public const string Spotify = "Spotify";
    public const string YouTubeMusic = "YouTubeMusic";
    public const string AppleMusic = "AppleMusic";
    public const string Deezer = "Deezer";
    public const string YandexMusic = "YandexMusic";
    public const string VkMusic = "VkMusic";
    // Add other keys if needed
}

// Конвертер для видимости TextBox
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is Visibility v && v == Visibility.Visible);
    }
}

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private AppConfig? _config; // Сделали nullable
    private readonly AppSettings _appSettings; // Добавлено
    private readonly SettingsService _settingsService; // Добавляем поле для SettingsService
    private DiscordRpcClient? _client;
    private HttpClient? _sharedHttpClient;
    private SpotifyService? _spotifyService;
    private DeezerService? _deezerService;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private List<GlobalSystemMediaTransportControlsSession> _allSessions = new();
    private bool _isComboBoxUpdate = false;
    private bool _isRpcConnected = false;
    private bool _isUpdatingSessionList = false;

    private DispatcherTimer? _presenceUpdateDebounceTimer;
    private const int DebounceMilliseconds = 100;

    private ApplicationTheme _currentAppTheme;

    // Словарь для связи ВНУТРЕННЕГО имени сайта и чекбокса
    private Dictionary<string, CheckBox> _linkCheckBoxes = new Dictionary<string, CheckBox>();

    // Список ВНУТРЕННИХ имен выбранных сайтов для кнопок (локальная копия)
    private List<string> _selectedLinkSites = new List<string>();
    private const int MaxLinkSites = 2;

    // --- Захардкоженные ключи (Base64) ---
    private const string EncodedClientId = "MTM2NTM5NzU2ODY3NDIwNTc4OA=="; // Base64 of "1365397568674205788"
    private const string EncodedLastFmApiKey = "NTU4MDdkZjA4MjhmMTdkNGY0YWYwZWJmYzU3Y2I4MTk="; // Base64 of "55807df0828f17d4f4af0ebfc57cb819"
    // -------------------------------------

    private bool _isCopyingDebugInfo = false; // Флаг, чтобы предотвратить повторный вход

    public AppSettings CurrentAppSettings => _appSettings; // Добавляем публичное свойство

    public MainWindow(AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _appSettings = settings; // Сохраняем настройки
        _settingsService = settingsService; // Сохраняем сервис настроек
        
        // Устанавливаем DataContext на сам объект MainWindow
        this.DataContext = this; 
        
        // --- Populate Cover Art Source ComboBox --- 
        PopulateCoverArtSourceComboBox(); 
        // -----------------------------------------
        
        // --- Получение и отображение версии --- 
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v?.?.?";
        }
        catch (Exception ex)
        {
            VersionTextBlock.Text = "v.err";
            DebugLogger.Log("Error getting application version", ex);
        }
        // ------------------------------------

        // --- Инициализация Config с захардкоженными значениями ---
        string clientId;
        string lastFmApiKey;
        try
        {
             clientId = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedClientId));
             lastFmApiKey = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedLastFmApiKey));
        }
        catch (FormatException ex)
        {
            System.Windows.MessageBox.Show($"Error decoding embedded configuration: {ex.Message}. Please contact the developer.", 
                                           "Configuration Error", 
                                           System.Windows.MessageBoxButton.OK, 
                                           System.Windows.MessageBoxImage.Error);
             Application.Current.Shutdown();
             return;
        }

        // Создаем объект _config, но используем ключи напрямую
        _config = new AppConfig(); // Можем потом сюда добавить чтение/сохранение других настроек из другого места

        // Проверяем валидность "захардкоженных" ключей (на всякий случай)
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(lastFmApiKey))
        {
             System.Windows.MessageBox.Show("Error: Embedded ClientId or LastFmApiKey is missing. Please contact the developer.", 
                                            "Configuration Error", 
                                            System.Windows.MessageBoxButton.OK, 
                                            System.Windows.MessageBoxImage.Error);
             Application.Current.Shutdown();
             return;
        }
        // -------------------------------------------------------


        // --- Начальная установка темы (можно будет потом тоже вынести в настройки, если нужно) --- 
        // string? preferredTheme = _config.PreferredTheme; // Пока убрали чтение темы из _config
        ApplicationTheme themeToApply;

        // if (!string.IsNullOrEmpty(preferredTheme) && Enum.TryParse(preferredTheme, true, out ApplicationTheme parsedTheme))
        // {
        //     themeToApply = parsedTheme;
        //     Debug.WriteLine($"Applying preferred theme from config: {themeToApply}");
        // }
        // else
        // {
            SystemTheme systemTheme = ApplicationThemeManager.GetSystemTheme();
            themeToApply = systemTheme switch
            {
                SystemTheme.Light => ApplicationTheme.Light,
                SystemTheme.Dark => ApplicationTheme.Dark,
                _ => ApplicationTheme.Dark 
            };
            DebugLogger.Log($"Applying system theme: {themeToApply} (Detected: {systemTheme})");
        // }

        ApplicationThemeManager.Apply(themeToApply);
        _currentAppTheme = themeToApply;
        ThemeCycleButton.Icon = (_currentAppTheme == ApplicationTheme.Dark) 
            ? new SymbolIcon(SymbolRegular.WeatherMoon24) 
            : new SymbolIcon(SymbolRegular.WeatherSunny24);
        // -------------------------------\


        // --- Инициализируем HttpClient и сервисы, используя раскодированные ключи и настройки ---
        _sharedHttpClient = new HttpClient();
        _spotifyService = new SpotifyService(_appSettings);
        _deezerService = new DeezerService(_appSettings);
        MediaStateManager.Instance.Initialize(_spotifyService, _deezerService, _appSettings);
        // --------------------------------------------------------------------------

        Dispatcher.InvokeAsync(InitializeMediaIntegration);
        InitializeDebounceTimer();
        this.Closed += MainWindow_Closed;
        
        // Инициализируем и загружаем состояние чекбоксов ссылок
        InitializeLinkCheckBoxes();
        LoadLinkCheckBoxStates();

        UpdateButtonStates();
        
        // Инициализируем сервис плавающего плеера, передавая ему настройки
        FloatingPlayerService.Instance.Initialize(_appSettings);
        
        // Подписываемся на события кнопок плавающего плеера
        FloatingPlayerService.Instance.PreviousRequested += FloatingPlayer_PreviousRequested;
        FloatingPlayerService.Instance.PlayPauseToggleRequested += FloatingPlayer_PlayPauseToggleRequested;
        FloatingPlayerService.Instance.NextRequested += FloatingPlayer_NextRequested;
    }

    // New method to populate the ComboBox
    private void PopulateCoverArtSourceComboBox()
    {
        var sources = new List<string>
        {
            // Use Properties.Resources directly for simplicity here
            // Alternatively, use LocalizationManager.GetString if preferred
            Properties.Resources.CoverArtSource_Deezer, 
            Properties.Resources.CoverArtSource_Spotify 
        };
        CoverArtSourceComboBox.ItemsSource = sources;
        
        // Ensure the initial SelectedValue matches the loaded setting
        // The binding on SelectedValue should handle this automatically if the string values match exactly.
        // If binding fails, uncomment the line below:
        // CoverArtSourceComboBox.SelectedValue = _appSettings.CoverArtSource; 
    }

    private void InitializeDiscord()
    {
        if (_currentSession == null)
        {
            // Use resource for message
            UpdateStatusText(LocalizationManager.GetString("Status_NoMediaSession")); 
            DebugLogger.Log("InitializeDiscord skipped: No source selected.");
            return;
        }

        if (_client != null && _client.IsInitialized)
        {
            DebugLogger.Log("Discord client already initialized.");
            return;
        }

        string clientId;
        try
        {
            clientId = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedClientId));
        }
        catch (FormatException ex)
        {
             // Use resource (consider adding a specific key)
             UpdateStatusText(string.Format(LocalizationManager.GetString("Status_Error"), "Invalid embedded ClientID"));
             System.Windows.MessageBox.Show($"Error decoding embedded ClientID: {ex.Message}. Please contact the developer.",
                                            "Configuration Error",
                                            System.Windows.MessageBoxButton.OK,
                                            System.Windows.MessageBoxImage.Error);
             return;
        }
        if (string.IsNullOrEmpty(clientId))
        {
            // Use resource (consider adding a specific key)
            UpdateStatusText(string.Format(LocalizationManager.GetString("Status_Error"), "ClientID is not configured (embedded)"));
            System.Windows.MessageBox.Show("Embedded Discord Client ID is missing. Please contact the developer.",
                                           "Configuration Error",
                                           System.Windows.MessageBoxButton.OK,
                                           System.Windows.MessageBoxImage.Error);
            return;
        }

        _client = new DiscordRpcClient(clientId);
        _client.Logger = new ConsoleLogger() { Level = LogLevel.Warning };

        _client.OnReady += (sender, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                DebugLogger.Log($"Received Ready from user {e.User.Username}");
                _isRpcConnected = true;
                // Use resource (consider adding a specific key for 'Connected as {username}')
                UpdateStatusText(string.Format(LocalizationManager.GetString("Status_ConnectedAsUser"), e.User.Username));
                UpdateButtonStates();
                RequestPresenceUpdateDebounced(); // Update presence immediately on connect
            });
        };

        _client.OnError += (sender, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Corrected logging call to use a single string argument
                DebugLogger.Log($"Discord RPC Error: {e.Message} (Code: {e.Code})"); 
                // Use resource
                UpdateStatusText(string.Format(LocalizationManager.GetString("Status_Error"), e.Message)); 
                DisconnectDiscord(); // Disconnect on error
            });
        };

        _client.OnConnectionFailed += (sender, e) =>
        {
             Dispatcher.InvokeAsync(() =>
            {
                DebugLogger.Log("Discord RPC Connection Failed");
                // Use resource (consider adding a specific key)
                UpdateStatusText("Failed to connect to Discord.");
                DisconnectDiscord(); // Ensure cleanup
            });
        };

        _client.OnConnectionEstablished += (sender, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                DebugLogger.Log("Discord RPC Connection Established");
                // Status text is usually handled by OnReady
            });
        };

        _client.OnClose += (sender, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                DebugLogger.Log("Discord RPC Connection Closed");
                // Use resource
                UpdateStatusText(LocalizationManager.GetString("Status_Disconnected")); 
                DisconnectDiscord(); // Ensure cleanup
            });
        };

        // Use resource
        UpdateStatusText(LocalizationManager.GetString("Status_Connecting")); 
        _client.Initialize();
    }

    private async void InitializeMediaIntegration()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager == null)
            {
                UpdateStatusText("Failed to get SMTC Session Manager.");
                return;
            }

            _sessionManager.SessionsChanged += SessionsChangedHandler;

            if (_config != null && !string.IsNullOrEmpty(_config.LastSelectedSourceAppId))
            {
                MediaStateManager.Instance.SelectedSourceAppId = _config.LastSelectedSourceAppId;
                DebugLogger.Log($"InitializeMediaIntegration: Loaded saved source ID \'{_config.LastSelectedSourceAppId}\' into MediaStateManager.");
            }
            else
            {
                 DebugLogger.Log("InitializeMediaIntegration: No saved source ID found or _config is null.");
            }

            await UpdateSessionList();
            UpdateStatusText(LocalizationManager.GetString("Status_MediaControlsInitialized"));
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"SMTC Initialization Error", ex);
            UpdateStatusText($"SMTC Error: {ex.Message}");
        }
    }

    private async void SessionsChangedHandler(GlobalSystemMediaTransportControlsSessionManager? sender, SessionsChangedEventArgs? args)
    {
        DebugLogger.Log("SessionsChangedHandler triggered.");
        // Проверяем флаг перед запуском обновления И СОСТОЯНИЕ ПОДКЛЮЧЕНИЯ
        if (!_isUpdatingSessionList && !_isRpcConnected)
        {
            DebugLogger.Log("SessionsChangedHandler: Calling UpdateSessionList.");
            await Dispatcher.InvokeAsync(UpdateSessionList);
        }
        else
        {
            DebugLogger.Log($"SessionsChangedHandler skipped: isUpdating={_isUpdatingSessionList}, isConnected={_isRpcConnected}");
        }
    }

    private async Task UpdateSessionList()
    {
        // Добавляем проверку на активное подключение в самом начале
        if (_isRpcConnected)
        {
             DebugLogger.Log("UpdateSessionList skipped: RPC is connected. Source list update is paused.");
             return;
        }
        
        // Блокируем повторный вход
        if (_isUpdatingSessionList) {
             DebugLogger.Log("UpdateSessionList skipped: Already running.");
             return;
        }
        _isUpdatingSessionList = true;

        if (_sessionManager == null) {
            DebugLogger.Log("UpdateSessionList skipped: Session manager is null.");
             _isUpdatingSessionList = false;
            return;
        }
        DebugLogger.Log("--- UpdateSessionList started ---");

        // Используем ?. 
        string? desiredAppId = MediaStateManager.Instance.SelectedSourceAppId;
        DebugLogger.Log($"UpdateSessionList: Desired AppId (from StateManager): {desiredAppId ?? "null"}");

        try
        {
            var sessions = _sessionManager.GetSessions();
            _allSessions = sessions?.ToList() ?? new List<GlobalSystemMediaTransportControlsSession>();
            // --- ЛОГИРОВАНИЕ СЕССИЙ ---
            var sessionIds = _allSessions.Select(s => s?.SourceAppUserModelId ?? "null_session_object").ToList();
            DebugLogger.Log($"UpdateSessionList: Found {_allSessions.Count} sessions. IDs: [{string.Join(", ", sessionIds)}]");
            // --------------------------

            _isComboBoxUpdate = true; // Ставим флаг, чтобы SelectionChanged не срабатывал при очистке/заполнении
            SessionComboBox.Items.Clear();

            if (!_allSessions.Any())
            {
                DebugLogger.Log("UpdateSessionList: No media sources detected.");
                SessionComboBox.Items.Add(new ComboBoxItem { Content = "No media sources detected", IsEnabled = false });
                SessionComboBox.IsEnabled = false;
                if (_currentSession != null)
                {
                    DebugLogger.Log("UpdateSessionList: Clearing current session as no sources detected.");
                    // Важно: не вызываем SwitchToSession отсюда, чтобы не было рекурсии или гонки.
                    // Очистка произойдет через установку SelectedIndex = -1 ниже.
                }
                 SessionComboBox.SelectedIndex = -1; // Явно указываем, что ничего не выбрано
                 _isComboBoxUpdate = false;
                 _isUpdatingSessionList = false;
                 UpdateButtonStates();
                return;
            }

            SessionComboBox.IsEnabled = !_isRpcConnected;

            // Создаем элементы для ComboBox
            var comboBoxItems = new List<ComboBoxItem>();
            foreach (var session in _allSessions)
            {
                if (session == null) continue;

                string displayName = await GetSessionDisplayName(session);
                comboBoxItems.Add(new ComboBoxItem
                {
                    Content = displayName,
                    ToolTip = displayName,
                    Tag = session
                });
            }

            // Добавляем элементы в ComboBox
            foreach (var item in comboBoxItems)
            {
                SessionComboBox.Items.Add(item);
            }

            // Определяем индекс для выбора
            int targetIndex = -1;

            // 1. Ищем желаемый AppId (из StateManager), если он задан
            if (desiredAppId != null)
            {
                bool foundDesiredId = false; // Флаг для логирования
                DebugLogger.Log($"UpdateSessionList: Attempting to find desired AppId '{desiredAppId}' in the current list...");
                for (int i = 0; i < comboBoxItems.Count; i++)
                {
                    if (comboBoxItems[i].Tag is GlobalSystemMediaTransportControlsSession s && s.SourceAppUserModelId == desiredAppId)
                    {
                        targetIndex = i;
                        foundDesiredId = true; // Нашли!
                        DebugLogger.Log($"UpdateSessionList: Found desired AppId '{desiredAppId}' at index {i}");
                        break;
                    }
                }
                 // Если желаемый ID был, но не нашелся в текущих сессиях, targetIndex останется -1
                 // Это значит, что источник временно пропал, НЕ выбираем ничего другого.
                 if (!foundDesiredId) // Используем флаг для точности лога
                 {
                     DebugLogger.Log($"UpdateSessionList: Desired AppId '{desiredAppId}' was NOT FOUND in the current list of sessions: [{string.Join(", ", sessionIds)}]. Will select nothing.");
                 }
            }
            else // 2. desiredAppId == null (т.е. пользователь ничего не выбирал или сбросил выбор)
            {   
                 DebugLogger.Log("UpdateSessionList: No desired AppId. Looking for current session or playing session...");
                // Сначала ищем текущую активную сессию (_currentSession), если она есть и присутствует в списке
                if (_currentSession != null)
                {
                    for (int i = 0; i < comboBoxItems.Count; i++)
                    {
                         if (comboBoxItems[i].Tag is GlobalSystemMediaTransportControlsSession s && s.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
                         {
                             targetIndex = i;
                             DebugLogger.Log($"UpdateSessionList: Found current session '{_currentSession.SourceAppUserModelId}' at index {i}");
                             break;
                         }
                    }
                }

                // Если текущая сессия не найдена (или ее не было), ищем первую играющую
                if (targetIndex == -1)
                {
                    DebugLogger.Log("UpdateSessionList: Current session not found or null. Looking for a playing session...");
                    for (int i = 0; i < comboBoxItems.Count; i++)
                    {
                        if (comboBoxItems[i].Tag is GlobalSystemMediaTransportControlsSession s &&
                            s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            targetIndex = i;
                            DebugLogger.Log($"UpdateSessionList: Found playing session '{s.SourceAppUserModelId}' at index {i}");
                            break;
                        }
                    }
                }

                // Если ничего не найдено, выбираем первую
                if (targetIndex == -1 && comboBoxItems.Count > 0)
                {
                    DebugLogger.Log("UpdateSessionList: No playing session found. Selecting first available.");
                    targetIndex = 0;
                }
            }

            // Ставим SelectedIndex. Если targetIndex = -1, ничего не будет выбрано.
            DebugLogger.Log($"UpdateSessionList: Final decision - Setting SelectedIndex to {targetIndex}");
            SessionComboBox.SelectedIndex = targetIndex;
            
            // Вызываем SwitchToSession ЯВНО здесь, так как SelectionChanged будет проигнорирован из-за флага _isComboBoxUpdate.
            // Это гарантирует, что _currentSession установится правильно ДО вызова UpdateButtonStates в finally.
            if (targetIndex >= 0 && comboBoxItems.Count > targetIndex && comboBoxItems[targetIndex].Tag is GlobalSystemMediaTransportControlsSession sessionToSelect)
            {
                DebugLogger.Log($"UpdateSessionList: Manually calling SwitchToSession for initially selected index {targetIndex} ({sessionToSelect.SourceAppUserModelId}) while _isComboBoxUpdate={_isComboBoxUpdate}");
                SwitchToSession(sessionToSelect);
            }
            else if (targetIndex == -1) // Если ничего не выбрано (или список пуст), очистим сессию
            {
                 DebugLogger.Log($"UpdateSessionList: Manually calling SwitchToSession(null) for index {targetIndex} while _isComboBoxUpdate={_isComboBoxUpdate}");
                 SwitchToSession(null);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"Error updating session list", ex);
            UpdateStatusText("Error updating session list.");
            // Попытка очистить состояние при ошибке
             try {
                _isComboBoxUpdate = true;
                SessionComboBox.Items.Clear();
                SessionComboBox.Items.Add(new ComboBoxItem { Content = "Error loading sources", IsEnabled = false });
                SessionComboBox.IsEnabled = false;
                SessionComboBox.SelectedIndex = -1;
                 if (_currentSession != null) {
                     MediaStateManager.Instance.SelectedSourceAppId = null;
                     _currentSession.MediaPropertiesChanged -= MediaPropertiesChangedHandler;
                     _currentSession.PlaybackInfoChanged -= PlaybackInfoChangedHandler;
                     _currentSession = null;
                     if (_isRpcConnected) _client?.ClearPresence();
                 }
             } catch (Exception cleanupEx) {
                 DebugLogger.Log($"Error during cleanup after session list update error", cleanupEx);
             }
        }
        finally
        {
            _isComboBoxUpdate = false; // Снимаем флаг для ComboBox
            _isUpdatingSessionList = false; // Снимаем флаг блокировки
            DebugLogger.Log("--- UpdateSessionList finished ---");
            UpdateButtonStates(); // Теперь этот вызов должен видеть актуальный _currentSession
        }
    }

    private async Task<string> GetSessionDisplayName(GlobalSystemMediaTransportControlsSession session)
    {
        if (session == null) return "Invalid Session";

        try
        {
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            
            // --- Получаем и очищаем имя источника --- 
            string rawAumid = session.SourceAppUserModelId;
            string name = "Unknown Source"; // Имя по умолчанию

            if (!string.IsNullOrEmpty(rawAumid))
            {
                // --- Добавляем обработку известных AUMID --- 
                switch (rawAumid.ToLowerInvariant()) // Сравниваем в нижнем регистре
                {
                    // Плееры
                    case "ru.yandex.desktop.music":
                        name = "Yandex Music";
                        break;
                    case "spotify": 
                    case var s when s.StartsWith("spotifyab.spotifymusic_"): 
                        name = "Spotify";
                        break;
                    case "aimp": 
                        name = "AIMP";
                        break;
                    case "foobar2000":
                        name = "foobar2000";
                        break;
                    case "vlc":
                    case var v when v.StartsWith("videolan.vlc_"): 
                        name = "VLC";
                        break;
                    case var it when it.StartsWith("itunes_"): // iTunes часто имеет постфикс
                         name = "iTunes";
                         break;
                    // Добавь сюда другие плееры
                    
                    // Браузеры (часто имеют постфикс профиля после '!')
                    case var chr when chr.StartsWith("chrome!"): // Стандартный Chrome?
                    case var chr_sx when chr_sx.StartsWith("chromium!"): // Chromium-based
                        name = "Chrome/Chromium";
                        break;
                    case var ff when ff.StartsWith("firefox!"):
                    case var ff_dev when ff_dev.StartsWith("firefoxdeveloperedition!"):
                    case var ff_nightly when ff_nightly.StartsWith("firefoxnightly!"):
                         name = "Firefox";
                         break;
                    case var edg when edg.StartsWith("microsoftedge.stable!"):
                    case var edg_legacy when edg_legacy.StartsWith("microsoft.microsoftedge!"): // Старый Edge
                        name = "Microsoft Edge";
                        break;
                    case var op when op.StartsWith("opera!"):
                    case var op_gx when op_gx.StartsWith("operagx!"):
                         name = "Opera";
                         break;
                    case var viv when viv.StartsWith("vivaldi!"): // Как раз твой случай
                    case var vivDot when vivDot.StartsWith("vivaldi."): // Добавляем проверку на точку
                         name = "Vivaldi";
                         break;
                    // Добавь сюда другие браузеры

                    default: 
                        // Если ID неизвестен, применяем старую логику с '!'
                        int bangIndex = rawAumid.IndexOf('!');
                        if (bangIndex > 0)
                        {
                            name = rawAumid.Substring(0, bangIndex);
                        }
                        else
                        {
                            name = rawAumid; // Оставляем как есть
                        }
                        break;
                }
                // -------------------------------------------
            }
            // ----------------------------------------

            if (mediaProperties != null && !string.IsNullOrEmpty(mediaProperties.Title))
            {
                 // Используем очищенное имя 'name'
                 if (!name.Equals(mediaProperties.Title, StringComparison.OrdinalIgnoreCase))
                    name += $" ({mediaProperties.Title})";
            }
            else if (name == "Unknown Source") // Проверяем именно очищенное имя
            {
                 // Если имя все еще неизвестно, добавляем хэш для уникальности
                 name += $" #{session.GetHashCode()}";
            }
            return name;
        }
        catch
        {
            return "Error getting name";
        }
    }

    private void ClearCurrentSession()
    {
         if (_currentSession != null)
         {
             _currentSession.MediaPropertiesChanged -= MediaPropertiesChangedHandler;
             _currentSession.PlaybackInfoChanged -= PlaybackInfoChangedHandler;
             _currentSession = null;
             if (_isRpcConnected) _client?.ClearPresence();
             UpdateStatusText("No media source selected or active.");
         }
    }

    private void InitializeDebounceTimer()
    {
        _presenceUpdateDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds)
        };
        _presenceUpdateDebounceTimer.Tick += async (sender, args) =>
        {
            DebugLogger.Log($"[TIMER ---ms] Tick event handler started.");
            if (sender is DispatcherTimer timer)
            {
                timer.Stop();
                var stopwatch = Stopwatch.StartNew();
                DebugLogger.Log($"[TIMER {stopwatch.ElapsedMilliseconds}ms] Debounce timer elapsed. Calling UpdatePresenceFromSession...");
                // Передаем актуальный список сайтов перед обновлением
                if (MediaStateManager.Instance.SelectedLinkSites != null) 
                {
                    MediaStateManager.Instance.SelectedLinkSites = new List<string>(_selectedLinkSites);
                    DebugLogger.Log($"[TIMER {stopwatch.ElapsedMilliseconds}ms] Updated MediaStateManager.SelectedLinkSites: {string.Join(", ", _selectedLinkSites)}");
                }
                await UpdatePresenceFromSession(stopwatch);
            }
        };
    }

    private void RequestPresenceUpdateDebounced()
    {
         if (_presenceUpdateDebounceTimer == null) return;

        _presenceUpdateDebounceTimer.Stop();
        _presenceUpdateDebounceTimer.Start();
        DebugLogger.Log($"[TIMER ---ms] Requested presence update (debounced for {DebounceMilliseconds}ms)");
    }

    private void MediaPropertiesChangedHandler(GlobalSystemMediaTransportControlsSession? sender, MediaPropertiesChangedEventArgs? args)
    {
        if (sender != null && _currentSession != null && sender.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
        {
            DebugLogger.Log($"[EVENT ---ms] MediaPropertiesChangedHandler: Properties changed for {sender.SourceAppUserModelId}");
            RequestPresenceUpdateDebounced();
        }
    }

    private void PlaybackInfoChangedHandler(GlobalSystemMediaTransportControlsSession? sender, PlaybackInfoChangedEventArgs? args)
    {
        if (sender != null && _currentSession != null && sender.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
        {
            DebugLogger.Log($"[EVENT ---ms] PlaybackInfoChangedHandler: Playback info changed for {sender.SourceAppUserModelId}");
            RequestPresenceUpdateDebounced();
        }
    }

    private async Task UpdatePresenceFromSession(Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew();
        var initialElapsedMs = stopwatch.ElapsedMilliseconds;
        DebugLogger.Log($"[UPDATE {initialElapsedMs}ms] UpdatePresenceFromSession started.");

        if (_currentSession == null || _client == null)
        {
            DebugLogger.Log($"[UPDATE {stopwatch.ElapsedMilliseconds}ms] UpdatePresenceFromSession: No current session or client. Clearing presence.");
            _client?.ClearPresence();
            // Сбрасываем кэш в менеджере состояний, если отключаемся
            MediaStateManager.Instance.SetLastSentPresence(null);
            return;
        }

        try
        {
            // Используем синглтон для получения Presence
            RichPresence? newPresence = await MediaStateManager.Instance.BuildRichPresenceAsync(_currentSession, stopwatch);
            var buildElapsedMs = stopwatch.ElapsedMilliseconds;
             DebugLogger.Log($"[UPDATE {buildElapsedMs}ms] Got presence from MediaStateManager. Details: {newPresence?.Details ?? "null"}");

            // Используем синглтон для проверки
            if (MediaStateManager.Instance.ShouldUpdatePresence(newPresence, stopwatch))
            {
                 DebugLogger.Log($"[UPDATE {stopwatch?.ElapsedMilliseconds}ms] Calling _client.SetPresence...");
                _client.SetPresence(newPresence);
                // Обновляем текст статуса на основе данных из newPresence, которые уже были подготовлены
                UpdateStatusText($"{newPresence?.Details ?? "Unknown"} - {newPresence?.State ?? "Stopped"}");
                
                // Обновляем плавающий плеер - ЭТОТ ВЫЗОВ ЛИШНИЙ, т.к. BuildRichPresenceAsync уже обновил плеер
                // DebugLogger.Log($"[UPDATE {stopwatch?.ElapsedMilliseconds}ms] Updating Floating Player: Title='{title}', Artist='{artist}', Status='{status}', Thumbnail: {thumbnail != null}"); // Лог с миниатюрой
                // FloatingPlayerService.Instance.UpdateContent(title, artist, status, thumbnail); // Передаем миниатюру
                
                DebugLogger.Log($"[UPDATE {stopwatch?.ElapsedMilliseconds}ms] SetPresence called successfully. Waiting for OnPresenceUpdate callback...");
            }
            else
            {
                DebugLogger.Log($"[UPDATE {stopwatch?.ElapsedMilliseconds}ms] Presence data hasn't changed. Skipping SetPresence call");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[UPDATE {stopwatch?.ElapsedMilliseconds}ms] Error updating presence", ex);
            UpdateStatusText("Error updating presence.");
        }
    }

    private void UpdateStatusText(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = text;
                DebugLogger.Log($"UI Status updated: {text}");
            }
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _client?.Dispose();
        _sessionManager = null; // Явно освобождаем ресурсы SMTC
        _sharedHttpClient?.Dispose();
        
        // Останавливаем и закрываем плавающий плеер
        FloatingPlayerService.Instance.Shutdown();
        
        // Закрываем приложение полностью
        Application.Current.Shutdown();
    }

    private void SwitchToSession(GlobalSystemMediaTransportControlsSession? newSession)
    {
        var previousSessionId = _currentSession?.SourceAppUserModelId ?? "null";
        var newSessionId = newSession?.SourceAppUserModelId ?? "null";

        if (previousSessionId == newSessionId) {
            DebugLogger.Log($"SwitchToSession called. Current: {previousSessionId}, New: {newSessionId}. Session is the same.");
            if (MediaStateManager.Instance.SelectedSourceAppId != newSessionId)
            {
                 DebugLogger.Log($"SwitchToSession [SYNC]: Session is same, but StateManager had different ID ('{MediaStateManager.Instance.SelectedSourceAppId}'). Syncing to '{newSessionId}'");
                 MediaStateManager.Instance.SelectedSourceAppId = newSessionId;
                 RequestPresenceUpdateDebounced(); 
            }
            return;
        }

        DebugLogger.Log($"SwitchToSession called. Current: {previousSessionId}, New: {newSessionId}. Switching...");

        if (_currentSession != null)
        {
            DebugLogger.Log($"SwitchToSession: Unsubscribing from events for {previousSessionId}");
            _currentSession.MediaPropertiesChanged -= MediaPropertiesChangedHandler;
            _currentSession.PlaybackInfoChanged -= PlaybackInfoChangedHandler;
        }

        _currentSession = newSession;
        if (MediaStateManager.Instance.SelectedSourceAppId != newSessionId)
        {
            MediaStateManager.Instance.SelectedSourceAppId = newSessionId;
        }

        if (_currentSession != null)
        {
            DebugLogger.Log($"SwitchToSession: Subscribing to events for {newSessionId}");
            _currentSession.MediaPropertiesChanged += MediaPropertiesChangedHandler;
            _currentSession.PlaybackInfoChanged += PlaybackInfoChangedHandler;
            DebugLogger.Log($"SwitchToSession: Set StateManager.SelectedSourceAppId to {newSessionId}");

            if (_isRpcConnected)
            {
                DebugLogger.Log("SwitchToSession: Requesting presence update for the new session...");
                // Передаем актуальный список сайтов перед обновлением
                if (MediaStateManager.Instance.SelectedLinkSites != null) 
                {
                    MediaStateManager.Instance.SelectedLinkSites = new List<string>(_selectedLinkSites);
                     DebugLogger.Log($"[SwitchToSession] Updated MediaStateManager.SelectedLinkSites: {string.Join(", ", _selectedLinkSites)}");
                }
                Dispatcher.InvokeAsync(() => UpdatePresenceFromSession());
            }
            DebugLogger.Log($"Switched to session: {newSessionId}");
        }
        else // newSession is null
        {
            DebugLogger.Log("SwitchToSession: Clearing current session (newSession is null)");
            if (_isRpcConnected && _client != null && _client.IsInitialized)
            {
                _client.ClearPresence();
                 DebugLogger.Log("SwitchToSession: Cleared Discord presence.");
            }
            MediaStateManager.Instance.SelectedSourceAppId = null;
            ClearCurrentSession();
        }
    }

    private void SessionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (_isComboBoxUpdate) // Игнорируем изменения во время заполнения списка
        {
            DebugLogger.Log($"SessionComboBox_SelectionChanged skipped: _isComboBoxUpdate={_isComboBoxUpdate}");
            return;
        }

        var selectedItem = SessionComboBox.SelectedItem as ComboBoxItem;
        DebugLogger.Log($"SessionComboBox_SelectionChanged triggered. Selected Item: '{selectedItem?.Content ?? "null"}', Index: {SessionComboBox.SelectedIndex}");

        if (selectedItem?.Tag is GlobalSystemMediaTransportControlsSession selectedSession)
        {
            DebugLogger.Log($"SessionComboBox_SelectionChanged: User selected session: {selectedSession.SourceAppUserModelId}");
            SwitchToSession(selectedSession); // Переключаемся на выбранную сессию
        }
        else if (SessionComboBox.SelectedIndex == -1 || selectedItem?.Content?.ToString() == "No media sources detected" || selectedItem?.Content?.ToString() == "Error loading sources")
        {
            DebugLogger.Log($"SessionComboBox_SelectionChanged: Selected placeholder or invalid item. Clearing session.");
            SwitchToSession(null); // Очищаем сессию
        }
        else
        {
            DebugLogger.Log($"SessionComboBox_SelectionChanged: Selected item has no valid session tag. Item: {selectedItem?.Content}. Doing nothing.");
            // Ничего не делаем, если выбран некорректный элемент (маловероятно)
        }

        UpdateButtonStates();
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        DebugLogger.Log("--- ConnectButton_Click called ---");
        InitializeDiscord();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DebugLogger.Log("--- DisconnectButton_Click called ---");
        DisconnectDiscord();
    }

    private void DisconnectDiscord()
    {
        DebugLogger.Log("DisconnectDiscord called.");
        if (_client != null)
        {
            DebugLogger.Log("Disposing Discord client...");
            _client.ClearPresence();
            _client.Dispose();
            _client = null;
            _isRpcConnected = false;
            // Сбрасываем кэш в менеджере состояний
            MediaStateManager.Instance.SetLastSentPresence(null);
            UpdateStatusText("Disconnected");
            DebugLogger.Log("Discord client disposed.");
        }
        else
        {
            DebugLogger.Log("DisconnectDiscord: Client was already null.");
        }
        UpdateButtonStates();
        // Просто скрываем окно плеера (НОВЫЙ СПОСОБ)
        FloatingPlayerService.Instance.HidePlayer(); 
    }

    private void UpdateButtonStates()
    {
        Dispatcher.InvokeAsync(() =>
        {
            bool sourceSelected = _currentSession != null;
            bool canConnect = sourceSelected && !_isRpcConnected;
            bool canDisconnect = _isRpcConnected;

            ConnectButton.IsEnabled = canConnect;
            DisconnectButton.IsEnabled = canDisconnect;

            // Блокируем выбор источника и настройки после подключения
            SessionComboBox.IsEnabled = !_isRpcConnected;

            // Блокируем отдельные элементы управления настройками
            bool settingsEnabled = !_isRpcConnected;
            EnableCoverArtCheckBox.IsEnabled = settingsEnabled;
            // ComboBox активен только если включен чекбокс EnableCoverArtCheckBox И настройки активны
            CoverArtSourceComboBox.IsEnabled = settingsEnabled && (EnableCoverArtCheckBox.IsChecked == true);
            UseCustomCoverCheckBox.IsEnabled = settingsEnabled;
            CustomCoverUrlTextBox.IsEnabled = settingsEnabled; // Видимость управляется отдельно, но доступность тоже блокируем

            // Блокируем Expander с чекбоксами ссылок
            LinkButtonsExpander.IsEnabled = settingsEnabled;
            // И сворачиваем его, если подключились
            if (!settingsEnabled) // settingsEnabled будет false, когда _isRpcConnected = true
            {
                LinkButtonsExpander.IsExpanded = false;
            }

            // Блокируем кнопку Настроек - ВСЕГДА АКТИВНА
            SettingsButton.IsEnabled = true;

            DebugLogger.Log($"UpdateButtonStates: sourceSelected={sourceSelected}, _isRpcConnected={_isRpcConnected}, canConnect={canConnect}, canDisconnect={canDisconnect}, SessionComboBox.IsEnabled={SessionComboBox.IsEnabled}, SettingsEnabled={settingsEnabled}");
        });
    }

    private void ThemeCycleButton_Click(object sender, RoutedEventArgs e)
    {
        var newTheme = _currentAppTheme == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(newTheme);
        _currentAppTheme = newTheme;
        
        ThemeCycleButton.Icon = (newTheme == ApplicationTheme.Dark) 
            ? new SymbolIcon(SymbolRegular.WeatherMoon24) 
            : new SymbolIcon(SymbolRegular.WeatherSunny24);
            
        DebugLogger.Log($"Theme changed to: {newTheme}");
    }

    private void DevelopersHyperlink_Click(object sender, RoutedEventArgs e)
    {
        AboutWindow aboutWindow = new AboutWindow();
        aboutWindow.Show(); // Открываем как обычное окно
    }

    // --- Логика сворачивания/разворачивания в трей ---
    
    private void MainWindow_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            this.Hide(); // Скрываем окно с панели задач
            MyNotifyIcon.Visibility = Visibility.Visible; // Показываем иконку в трее
        }
        else
        {
            // При любом другом состоянии (Normal, Maximized) убеждаемся, что иконка скрыта,
            // если мы хотим, чтобы она была видна ТОЛЬКО при сворачивании.
            // Если иконка должна быть видна всегда, эту строку можно убрать.
            // MyNotifyIcon.Visibility = Visibility.Collapsed; 
        }
    }

    private void NotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowAndActivateWindow();
    }

    private void MenuItemShow_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivateWindow();
    }

    private void MenuItemExit_Click(object sender, RoutedEventArgs e)
    {
        MyNotifyIcon.Dispose(); // Освобождаем ресурсы иконки
        Application.Current.Shutdown();
    }
    
    private void ShowAndActivateWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate(); // Делаем окно активным
        MyNotifyIcon.Visibility = Visibility.Collapsed; // Скрываем иконку при разворачивании
    }
    
    // --- Переопределение закрытия окна (опционально) ---
    // Если раскомментировать, кнопка "Закрыть" будет сворачивать в трей
    /*
    protected override void OnClosing(CancelEventArgs e)
    {
        // Вместо закрытия сворачиваем в трей
        e.Cancel = true; // Отменяем закрытие
        WindowState = WindowState.Minimized;
        base.OnClosing(e);
    }
    */
    // ---------------------------------------------------

    // Инициализация словаря чекбоксов с использованием ВНУТРЕННИХ ключей
    private void InitializeLinkCheckBoxes()
    {
        _linkCheckBoxes = new Dictionary<string, CheckBox>
        {
            // Используем константы LinkButtonKeys как ключи
            { LinkButtonKeys.GitHub, GitHubLinkCheckBox },
            { LinkButtonKeys.Genius, GeniusLinkCheckBox },
            { LinkButtonKeys.Spotify, SpotifyLinkCheckBox }, // Предполагаем, что SpotifyLinkCheckBox существует
            { LinkButtonKeys.YouTubeMusic, YouTubeMusicLinkCheckBox }, // Предполагаем, что YouTubeMusicLinkCheckBox существует
            { LinkButtonKeys.AppleMusic, AppleMusicLinkCheckBox }, // Предполагаем, что AppleMusicLinkCheckBox существует
            { LinkButtonKeys.Deezer, DeezerLinkCheckBox }, // Предполагаем, что DeezerLinkCheckBox существует
            { LinkButtonKeys.YandexMusic, YandexMusicLinkCheckBox }, // Предполагаем, что YandexMusicLinkCheckBox существует
            { LinkButtonKeys.VkMusic, VkMusicLinkCheckBox }
            // Добавить другие, если они есть
        };

        // Устанавливаем Content для каждого чекбокса из локализации
        // и сохраняем внутренний ключ в Tag для обратного поиска
        foreach (var kvp in _linkCheckBoxes)
        {
            string internalKey = kvp.Key;
            CheckBox checkBox = kvp.Value;
            string resourceKey = $"LinkButton_{internalKey}"; // Генерируем ключ ресурса (LinkButton_VkMusic)
            checkBox.Content = LocalizationManager.GetString(resourceKey) ?? internalKey; // Устанавливаем локализованный текст
            checkBox.Tag = internalKey; // Сохраняем внутренний ключ в Tag
            DebugLogger.Log($"[InitializeLinkCheckBoxes] Initialized checkbox: Key='{internalKey}', Content='{checkBox.Content}', Tag='{checkBox.Tag}'");
        }
    }

    // Загрузка состояния чекбоксов из настроек (используем ВНУТРЕННИЕ ключи)
    private void LoadLinkCheckBoxStates()
    {
        _selectedLinkSites.Clear(); // Очищаем локальный список
        // Теперь savedSites будет содержать ВНУТРЕННИЕ ключи (GitHub, VkMusic и т.д.)
        var savedSites = _appSettings.SelectedLinkButtonSites ?? new List<string>();
        DebugLogger.Log($"[LoadLinkCheckBoxStates] Loading saved internal site keys: [{string.Join(", ", savedSites)}]");

        foreach (var cbPair in _linkCheckBoxes)
        {
            // Сравниваем ВНУТРЕННИЙ ключ словаря (cbPair.Key) с ВНУТРЕННИМИ ключами из настроек
            string internalKey = cbPair.Key;
            bool shouldBeChecked = savedSites.Contains(internalKey);
            cbPair.Value.IsChecked = shouldBeChecked;
            if (shouldBeChecked)
            {
                // Добавляем ВНУТРЕННИЙ ключ в локальный список
                if (!_selectedLinkSites.Contains(internalKey))
                {
                    _selectedLinkSites.Add(internalKey); 
                }
            }
            DebugLogger.Log($"[LoadLinkCheckBoxStates] Checkbox for internal key '{internalKey}' (Content: '{cbPair.Value.Content}') set to IsChecked={shouldBeChecked}");
        }
        
        // Обновляем состояние доступности после загрузки
        UpdateLinkCheckBoxStates();
        DebugLogger.Log($"[LoadLinkCheckBoxStates] Current local selection (internal keys): [{string.Join(", ", _selectedLinkSites)}]");
    }

    // Обработчик для чекбоксов ссылок (используем Tag для получения ВНУТРЕННЕГО ключа)
    private void LinkCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        DebugLogger.Log("[LinkCheckBox_Changed] Handler started.");
        if (sender is not CheckBox clickedCheckBox)
        {
            DebugLogger.Log("[LinkCheckBox_Changed] Handler exited: Invalid sender.");
            return;
        }

        // Получаем ВНУТРЕННИЙ ключ из Tag
        string? siteInternalKey = clickedCheckBox.Tag as string;
        if (string.IsNullOrEmpty(siteInternalKey)) 
        {
            DebugLogger.Log($"[LinkCheckBox_Changed] Handler exited: Could not get internal key from CheckBox Tag. Content was: '{clickedCheckBox.Content}'.");
            return;
        }
        
        DebugLogger.Log($"[LinkCheckBox_Changed] Checkbox for internal key '{siteInternalKey}' (Content: '{clickedCheckBox.Content}') changed. IsChecked: {clickedCheckBox.IsChecked}");

        if (clickedCheckBox.IsChecked == true)
        {
            if (_selectedLinkSites.Count < MaxLinkSites)
            {
                // Добавляем ВНУТРЕННИЙ ключ в существующий список
                if (!_appSettings.SelectedLinkButtonSites.Contains(siteInternalKey)) // Проверяем основной список
                {
                    _appSettings.SelectedLinkButtonSites.Add(siteInternalKey); 
                }
                 // Обновляем и локальную копию
                 if (!_selectedLinkSites.Contains(siteInternalKey))
                 {
                     _selectedLinkSites.Add(siteInternalKey);
                 }
            }
            else // Уже выбрано максимальное количество
            {
                clickedCheckBox.IsChecked = false; 
                System.Windows.MessageBox.Show($"You can select up to {MaxLinkSites} sites.", "Selection Limit Reached", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return; 
            }
        }
        else // Checkbox был снят
        {
            // Удаляем ВНУТРЕННИЙ ключ из существующего списка
            _appSettings.SelectedLinkButtonSites.Remove(siteInternalKey);
            // Обновляем и локальную копию (для консистентности, хотя она теперь менее важна)
             _selectedLinkSites.Remove(siteInternalKey);
        }
        
        // --- Сохранение в настройки БОЛЬШЕ НЕ НУЖНО ЗДЕСЬ, будет сохранено при выходе --- 
        // _appSettings.SelectedLinkButtonSites = new List<string>(_selectedLinkSites); 
        DebugLogger.Log($"[LinkCheckBox_Changed] Current internal site keys in AppSettings: [{string.Join(", ", _appSettings.SelectedLinkButtonSites)}]");
        // ---------------------------------------------------------------------------------

        UpdateLinkCheckBoxStates();
        
        if (_isRpcConnected) 
        {
             DebugLogger.Log("[LinkCheckBox_Changed] RPC is connected, requesting presence update.");
             // Передаем актуальный список ВНУТРЕННИХ ключей
             if (MediaStateManager.Instance != null) // Проверка на null для безопасности
             {
                 MediaStateManager.Instance.SelectedLinkSites = new List<string>(_selectedLinkSites);
                 DebugLogger.Log($"[LinkCheckBox_Changed] Updated MediaStateManager.SelectedLinkSites with internal keys: [{string.Join(", ", _selectedLinkSites)}]");
             }
             RequestPresenceUpdateDebounced(); 
             DebugLogger.Log("[LinkCheckBox_Changed] RequestPresenceUpdateDebounced was called.");
        }
        else
        {
            DebugLogger.Log("[LinkCheckBox_Changed] RPC is not connected, skipping presence update request.");
        }
    }

    // Метод для обновления состояния Enabled чекбоксов ссылок
    private void UpdateLinkCheckBoxStates()
    {
        bool limitReached = _selectedLinkSites.Count >= MaxLinkSites;
        
        // Используем словарь для обновления состояния
        foreach(var cbPair in _linkCheckBoxes)
        {
             cbPair.Value.IsEnabled = limitReached ? cbPair.Value.IsChecked == true : true;
        }
        // Старый код с прямым доступом по именам больше не нужен
        // SpotifyLinkCheckBox.IsEnabled = ... 
        // ... и т.д.
    }

    private void LoadAppSettings()
    {
        // ... existing code ...
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Передаем _appSettings и _settingsService в окно настроек
        var settingsWindow = new SettingsWindow(_appSettings, _settingsService) 
        {
            Owner = this // Устанавливаем владельца, чтобы окно настроек было модальным по отношению к главному
        };
        settingsWindow.ShowDialog(); // Показываем как модальное диалоговое окно
        // После закрытия окна настроек, можно обновить что-то, если нужно
        // Например, обновить DataContext главного окна, если настройки влияют на него
        // this.DataContext = new { Settings = _appSettings }; // Перепривязка, если нужно
        // UpdateButtonStates(); // Обновить состояния кнопок, если они зависят от AppSettings
    }

    // --- Обработчики событий от плавающего плеера ---
    private async void FloatingPlayer_PreviousRequested(object? sender, EventArgs e)
    {
        if (_currentSession != null)
        {
            try
            {
                DebugLogger.Log("Floating Player: Previous requested.");
                await _currentSession.TrySkipPreviousAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("Error calling TrySkipPreviousAsync", ex);
            }
        }
    }

    private async void FloatingPlayer_PlayPauseToggleRequested(object? sender, EventArgs e)
    {
         if (_currentSession != null)
        {
            try
            {
                DebugLogger.Log("Floating Player: Play/Pause requested.");
                await _currentSession.TryTogglePlayPauseAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("Error calling TryTogglePlayPauseAsync", ex);
            }
        }
    }

    private async void FloatingPlayer_NextRequested(object? sender, EventArgs e)
    {
         if (_currentSession != null)
        {
            try
            {
                DebugLogger.Log("Floating Player: Next requested.");
                await _currentSession.TrySkipNextAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("Error calling TrySkipNextAsync", ex);
            }
        }
    }
    // -----------------------------------------------

    // Новый обработчик для кнопки копирования
    private async void CopyDebugInfoButton_Click(object sender, RoutedEventArgs e)
    {
        // Предотвращаем повторный запуск, если копирование уже идет
        if (_isCopyingDebugInfo)
        {
            return;
        }

        _isCopyingDebugInfo = true;
        // Указываем полный тип System.Windows.Controls.Button
        var button = (System.Windows.Controls.Button)sender; 
        var originalContent = button.Content;
        var originalToolTip = button.ToolTip;
        button.IsEnabled = false; // Отключаем кнопку

        try
        {
            string clientId = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedClientId));
            Clipboard.SetText(clientId);

            // Успех
            button.Content = "✓";
            button.ToolTip = "Скопировано!";
        }
        catch (Exception ex)
        {
            DebugLogger.Log("Error copying debug info to clipboard", ex);
            // Ошибка
            button.Content = "X";
            button.ToolTip = "Ошибка копирования";
        }
        finally // Этот блок выполнится всегда: и после try, и после catch
        {
            // Ждем 1.5 секунды перед восстановлением
            await Task.Delay(500);

            // Восстанавливаем исходное состояние и включаем кнопку
            // Проверяем Dispatcher, так как Task.Delay может завершиться в другом потоке
            if (button.Dispatcher.CheckAccess())
            {
                button.Content = originalContent;
                button.ToolTip = originalToolTip;
                button.IsEnabled = true;
                _isCopyingDebugInfo = false; // Сбрасываем флаг
            }
            else
            {
                button.Dispatcher.Invoke(() =>
                {
                    button.Content = originalContent;
                    button.ToolTip = originalToolTip;
                    button.IsEnabled = true;
                    _isCopyingDebugInfo = false; // Сбрасываем флаг
                });
            }
        }
    }

    // --- Вспомогательный класс для десериализации релизов GitHub ---
    // Можно вынести в отдельный файл Models/GitHubRelease.cs, если хочешь
    public class GitHubRelease
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; } // Ссылка на релиз
    }
    // -------------------------------------------------------------

    private async void VersionTextBlock_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DebugLogger.Log("VersionTextBlock clicked. Fetching changelog...");
        // Use resource for status
        UpdateStatusText(LocalizationManager.GetString("Changelog_StatusLoading")); 
        this.Cursor = System.Windows.Input.Cursors.Wait; // Меняем курсор

        ChangelogWindow? changelogWindow = null;

        try
        {
            var httpClient = _sharedHttpClient ?? new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("CustomMediaRPC-App");

            string repoUrl = "https://api.github.com/repos/RaspizDIYs/CustomRPC/releases";
            HttpResponseMessage response = await httpClient.GetAsync(repoUrl);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(jsonResponse);

                if (releases != null && releases.Count > 0)
                {
                    // Успешно получили релизы, передаем список
                    await Dispatcher.InvokeAsync(() =>
                    {
                        changelogWindow = new ChangelogWindow(releases.Take(10).ToList()) // Берем последние 10
                        {
                            Owner = this
                        };
                    });
                }
                else
                {
                    // Нет релизов - use resource
                    string noReleasesMsg = LocalizationManager.GetString("Changelog_ErrorNoReleases");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        changelogWindow = new ChangelogWindow(noReleasesMsg)
                        {
                            Owner = this
                        };
                    });
                }
            }
            else
            {
                // Ошибка HTTP - use resource
                string errorDetails = await response.Content.ReadAsStringAsync();
                string httpErrorMsg = string.Format(LocalizationManager.GetString("Changelog_ErrorHttp"), response.StatusCode, errorDetails);
                await Dispatcher.InvokeAsync(() =>
                {
                    changelogWindow = new ChangelogWindow(httpErrorMsg)
                    {
                        Owner = this
                    };
                });
            }
        }
        catch (HttpRequestException httpEx)
        {
            DebugLogger.Log("Error fetching changelog (HTTP)", httpEx);
            // Use resource
            string networkErrorMsg = string.Format(LocalizationManager.GetString("Changelog_ErrorNetwork"), httpEx.Message);
            await Dispatcher.InvokeAsync(() => { changelogWindow = new ChangelogWindow(networkErrorMsg) { Owner = this }; });
        }
        catch (JsonException jsonEx)
        {
             DebugLogger.Log("Error parsing changelog JSON", jsonEx);
             // Use resource
             string parsingErrorMsg = string.Format(LocalizationManager.GetString("Changelog_ErrorParsing"), jsonEx.Message);
             await Dispatcher.InvokeAsync(() => { changelogWindow = new ChangelogWindow(parsingErrorMsg) { Owner = this }; });
        }
        catch (Exception ex)
        {
            DebugLogger.Log("Error fetching changelog (General)", ex);
             // Use resource
            string unexpectedErrorMsg = string.Format(LocalizationManager.GetString("Changelog_ErrorUnexpected"), ex.Message);
            await Dispatcher.InvokeAsync(() => { changelogWindow = new ChangelogWindow(unexpectedErrorMsg) { Owner = this }; });
        }
        finally
        {
             // Восстанавливаем статус и курсор
            // Restore previous status (might need to store it)
            // For now, just clear or set to default connected/disconnected
             UpdateStatusText(_isRpcConnected ? LocalizationManager.GetString("Status_Connected") : LocalizationManager.GetString("Status_Disconnected"));
            this.Cursor = null; // Возвращаем обычный курсор

            // Показываем окно, если оно было создано
            await Dispatcher.InvokeAsync(() =>
            {
                changelogWindow?.Show();
            });
        }
    }
}