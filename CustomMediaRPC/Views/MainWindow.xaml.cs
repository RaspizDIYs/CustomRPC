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
using CustomMediaRPC.Services;
using System.Windows;
using System.Windows.Controls;
using DiscordRPC;
using DiscordRPC.Logging;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Documents;
using Wpf.Ui.Controls;
using System.Text; // Добавим для Base64
using System.ComponentModel; // Добавили для Closing

namespace CustomMediaRPC.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private AppConfig? _config; // Сделали nullable
    private DiscordRpcClient? _client;
    private MediaStateManager? _mediaStateManager;
    private HttpClient? _sharedHttpClient;
    private LastFmService? _lastFmService;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private List<GlobalSystemMediaTransportControlsSession> _allSessions = new();
    private bool _isComboBoxUpdate = false;
    private bool _isRpcConnected = false;
    private bool _isUpdatingSessionList = false;

    private DispatcherTimer? _presenceUpdateDebounceTimer;
    private const int DebounceMilliseconds = 300;

    private ApplicationTheme _currentAppTheme;

    // --- Захардкоженные ключи (Base64) ---
    private const string EncodedClientId = "MTM2NTM5NzU2ODY3NDIwNTc4OA=="; // Base64 of "1365397568674205788"
    private const string EncodedLastFmApiKey = "NTU4MDdkZjA4MjhmMTdkNGY0YWYwZWJmYzU3Y2I4MTk="; // Base64 of "55807df0828f17d4f4af0ebfc57cb819"
    // -------------------------------------

    public MainWindow()
    {
        InitializeComponent();
        
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
            Debug.WriteLine($"Applying system theme: {themeToApply} (Detected: {systemTheme})");
        // }

        ApplicationThemeManager.Apply(themeToApply);
        _currentAppTheme = themeToApply;
        ThemeCycleButton.Icon = (_currentAppTheme == ApplicationTheme.Dark) 
            ? new SymbolIcon(SymbolRegular.WeatherMoon24) 
            : new SymbolIcon(SymbolRegular.WeatherSunny24);
        // -------------------------------\


        // --- Инициализируем HttpClient и сервисы, используя раскодированные ключи ---
        _sharedHttpClient = new HttpClient();
        _lastFmService = new LastFmService(_sharedHttpClient, lastFmApiKey); // Используем переменную
        _mediaStateManager = new MediaStateManager(_lastFmService);
        // --------------------------------------------------------------------------

        Dispatcher.InvokeAsync(InitializeMediaIntegration);
        InitializeDebounceTimer();
        this.Closed += MainWindow_Closed;

        UpdateButtonStates();
    }

    private void InitializeDiscord()
    {
        if (_currentSession == null) 
        {
            UpdateStatusText("Please select a media source first.");
            Debug.WriteLine("InitializeDiscord skipped: No source selected.");
            return;
        }

        if (_client != null && _client.IsInitialized)
        {
            Debug.WriteLine("Discord client already initialized.");
            return;
        }
        
        // --- Получаем Client ID ---
        string clientId;
        try
        {
            clientId = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedClientId));
        }
        catch (FormatException ex)
        {
             UpdateStatusText("Error: Invalid embedded ClientID.");
             System.Windows.MessageBox.Show($"Error decoding embedded ClientID: {ex.Message}. Please contact the developer.", 
                                            "Configuration Error", 
                                            System.Windows.MessageBoxButton.OK, 
                                            System.Windows.MessageBoxImage.Error);
             return;
        }
        if (string.IsNullOrEmpty(clientId))
        {
            UpdateStatusText("Error: ClientID is not configured (embedded).");
            System.Windows.MessageBox.Show("Embedded Discord Client ID is missing. Please contact the developer.", 
                                           "Configuration Error", 
                                           System.Windows.MessageBoxButton.OK, 
                                           System.Windows.MessageBoxImage.Error);
            return;
        }
        // -------------------------

        _client = new DiscordRpcClient(clientId); // Используем раскодированный clientId
        _client.Logger = new ConsoleLogger() { Level = LogLevel.Warning };

        _client.OnReady += (sender, e) =>
        {
            Debug.WriteLine($"Received Ready from user {e.User.Username}");
            Dispatcher.InvokeAsync(async () => { 
                try 
                {
                    _isRpcConnected = true;
                    UpdateStatusText($"Connected as {e.User.Username}");
                    UpdateButtonStates();
                    // Обновляем статус сразу после подключения
                    if (_currentSession != null)
                    {
                        await UpdatePresenceFromSession();
                    }
                    Debug.WriteLine("OnReady Dispatcher actions completed successfully.");
                }
                catch (Exception ex)
                {
                     Debug.WriteLine($"!!! EXCEPTION in OnReady Dispatcher: {ex.ToString()}");
                }
            });
        };

        _client.OnPresenceUpdate += (sender, e) =>
        {
            Debug.WriteLine($"Presence updated!");
        };

        _client.OnError += (sender, e) => {
            Debug.WriteLine($"Discord Error: {e.Message}");
            UpdateStatusText($"Discord Error: {e.Code}");
        };

        _client.Initialize();
        UpdateStatusText("Connecting to Discord...");
        UpdateButtonStates();
    }

    private async void InitializeMediaIntegration()
    {
        try
        {
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager == null)
            {
                UpdateStatusText("Failed to get SMTC Session Manager.");
                return;
            }

            _sessionManager.SessionsChanged += SessionsChangedHandler;

            if (!string.IsNullOrEmpty(_config.LastSelectedSourceAppId))
            {
                if (_mediaStateManager != null) 
                {
                    _mediaStateManager.SelectedSourceAppId = _config.LastSelectedSourceAppId;
                }
                Debug.WriteLine($"InitializeMediaIntegration: Loaded saved source ID '{_config.LastSelectedSourceAppId}' into MediaStateManager.");
            }
            else
            {
                 Debug.WriteLine("InitializeMediaIntegration: No saved source ID found.");
            }

            await UpdateSessionList();
            UpdateStatusText("Media Controls initialized. Select a source or wait for media...");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SMTC Initialization Error: {ex.Message}");
            UpdateStatusText($"SMTC Error: {ex.Message}");
        }
    }

    private async void SessionsChangedHandler(GlobalSystemMediaTransportControlsSessionManager? sender, SessionsChangedEventArgs? args)
    {
        Debug.WriteLine("SessionsChangedHandler triggered.");
        // Проверяем флаг перед запуском обновления И СОСТОЯНИЕ ПОДКЛЮЧЕНИЯ
        if (!_isUpdatingSessionList && !_isRpcConnected)
        {
            Debug.WriteLine("SessionsChangedHandler: Calling UpdateSessionList.");
            await Dispatcher.InvokeAsync(UpdateSessionList);
        }
        else
        {
            Debug.WriteLine($"SessionsChangedHandler skipped: isUpdating={_isUpdatingSessionList}, isConnected={_isRpcConnected}");
        }
    }

    private async Task UpdateSessionList()
    {
        // Добавляем проверку на активное подключение в самом начале
        if (_isRpcConnected)
        {
             Debug.WriteLine("UpdateSessionList skipped: RPC is connected. Source list update is paused.");
             return;
        }
        
        // Блокируем повторный вход
        if (_isUpdatingSessionList) {
             Debug.WriteLine("UpdateSessionList skipped: Already running.");
             return;
        }
        _isUpdatingSessionList = true;

        if (_sessionManager == null) {
            Debug.WriteLine("UpdateSessionList skipped: Session manager is null.");
             _isUpdatingSessionList = false;
            return;
        }
        Debug.WriteLine("--- UpdateSessionList started ---");

        // Используем ?. 
        string? desiredAppId = _mediaStateManager?.SelectedSourceAppId;
        Debug.WriteLine($"UpdateSessionList: Desired AppId (from StateManager): {desiredAppId ?? "null"}");

        try
        {
            var sessions = _sessionManager.GetSessions();
            _allSessions = sessions?.ToList() ?? new List<GlobalSystemMediaTransportControlsSession>();
            // --- ЛОГИРОВАНИЕ СЕССИЙ ---
            var sessionIds = _allSessions.Select(s => s?.SourceAppUserModelId ?? "null_session_object").ToList();
            Debug.WriteLine($"UpdateSessionList: Found {_allSessions.Count} sessions. IDs: [{string.Join(", ", sessionIds)}]");
            // --------------------------

            _isComboBoxUpdate = true; // Ставим флаг, чтобы SelectionChanged не срабатывал при очистке/заполнении
            SessionComboBox.Items.Clear();

            if (!_allSessions.Any())
            {
                Debug.WriteLine("UpdateSessionList: No media sources detected.");
                SessionComboBox.Items.Add(new ComboBoxItem { Content = "No media sources detected", IsEnabled = false });
                SessionComboBox.IsEnabled = false;
                if (_currentSession != null)
                {
                    Debug.WriteLine("UpdateSessionList: Clearing current session as no sources detected.");
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
                Debug.WriteLine($"UpdateSessionList: Attempting to find desired AppId '{desiredAppId}' in the current list...");
                for (int i = 0; i < comboBoxItems.Count; i++)
                {
                    if (comboBoxItems[i].Tag is GlobalSystemMediaTransportControlsSession s && s.SourceAppUserModelId == desiredAppId)
                    {
                        targetIndex = i;
                        foundDesiredId = true; // Нашли!
                        Debug.WriteLine($"UpdateSessionList: Found desired AppId '{desiredAppId}' at index {i}");
                        break;
                    }
                }
                 // Если желаемый ID был, но не нашелся в текущих сессиях, targetIndex останется -1
                 // Это значит, что источник временно пропал, НЕ выбираем ничего другого.
                 if (!foundDesiredId) // Используем флаг для точности лога
                 {
                     Debug.WriteLine($"UpdateSessionList: Desired AppId '{desiredAppId}' was NOT FOUND in the current list of sessions: [{string.Join(", ", sessionIds)}]. Will select nothing.");
                 }
            }
            else // 2. desiredAppId == null (т.е. пользователь ничего не выбирал или сбросил выбор)
            {   
                 Debug.WriteLine("UpdateSessionList: No desired AppId. Looking for current session or playing session...");
                // Сначала ищем текущую активную сессию (_currentSession), если она есть и присутствует в списке
                if (_currentSession != null)
                {
                    for (int i = 0; i < comboBoxItems.Count; i++)
                    {
                         if (comboBoxItems[i].Tag is GlobalSystemMediaTransportControlsSession s && s.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
                         {
                             targetIndex = i;
                             Debug.WriteLine($"UpdateSessionList: Found current session '{_currentSession.SourceAppUserModelId}' at index {i}");
                             break;
                         }
                    }
                }

                // Если текущая сессия не найдена (или ее не было), ищем первую играющую
                if (targetIndex == -1)
                {
                    Debug.WriteLine("UpdateSessionList: Current session not found or null. Looking for a playing session...");
                    for (int i = 0; i < comboBoxItems.Count; i++)
                    {
                        if (comboBoxItems[i].Tag is GlobalSystemMediaTransportControlsSession s &&
                            s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            targetIndex = i;
                            Debug.WriteLine($"UpdateSessionList: Found playing session '{s.SourceAppUserModelId}' at index {i}");
                            break;
                        }
                    }
                }

                // Если ничего не найдено, выбираем первую
                if (targetIndex == -1 && comboBoxItems.Count > 0)
                {
                    Debug.WriteLine("UpdateSessionList: No playing session found. Selecting first available.");
                    targetIndex = 0;
                }
            }

            // Ставим SelectedIndex. Если targetIndex = -1, ничего не будет выбрано.
            Debug.WriteLine($"UpdateSessionList: Final decision - Setting SelectedIndex to {targetIndex}");
            SessionComboBox.SelectedIndex = targetIndex;
            
            // Вызываем SwitchToSession ЯВНО здесь, так как SelectionChanged будет проигнорирован из-за флага _isComboBoxUpdate.
            // Это гарантирует, что _currentSession установится правильно ДО вызова UpdateButtonStates в finally.
            if (targetIndex >= 0 && comboBoxItems.Count > targetIndex && comboBoxItems[targetIndex].Tag is GlobalSystemMediaTransportControlsSession sessionToSelect)
            {
                Debug.WriteLine($"UpdateSessionList: Manually calling SwitchToSession for initially selected index {targetIndex} ({sessionToSelect.SourceAppUserModelId}) while _isComboBoxUpdate={_isComboBoxUpdate}");
                SwitchToSession(sessionToSelect);
            }
            else if (targetIndex == -1) // Если ничего не выбрано (или список пуст), очистим сессию
            {
                 Debug.WriteLine($"UpdateSessionList: Manually calling SwitchToSession(null) for index {targetIndex} while _isComboBoxUpdate={_isComboBoxUpdate}");
                 SwitchToSession(null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating session list: {ex}");
            UpdateStatusText("Error updating session list.");
            // Попытка очистить состояние при ошибке
             try {
                _isComboBoxUpdate = true;
                SessionComboBox.Items.Clear();
                SessionComboBox.Items.Add(new ComboBoxItem { Content = "Error loading sources", IsEnabled = false });
                SessionComboBox.IsEnabled = false;
                SessionComboBox.SelectedIndex = -1;
                 if (_currentSession != null) {
                     if (_mediaStateManager != null)
                     {
                        _mediaStateManager.SelectedSourceAppId = null;
                     }
                     _currentSession.MediaPropertiesChanged -= MediaPropertiesChangedHandler;
                     _currentSession.PlaybackInfoChanged -= PlaybackInfoChangedHandler;
                     _currentSession = null;
                     if (_isRpcConnected) _client?.ClearPresence();
                 }
             } catch (Exception cleanupEx) {
                 Debug.WriteLine($"Error during cleanup after session list update error: {cleanupEx}");
             }
        }
        finally
        {
            _isComboBoxUpdate = false; // Снимаем флаг для ComboBox
            _isUpdatingSessionList = false; // Снимаем флаг блокировки
            Debug.WriteLine("--- UpdateSessionList finished ---");
            UpdateButtonStates(); // Теперь этот вызов должен видеть актуальный _currentSession
        }
    }

    private async Task<string> GetSessionDisplayName(GlobalSystemMediaTransportControlsSession session)
    {
        if (session == null) return "Invalid Session";

        try
        {
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            string name = !string.IsNullOrEmpty(session.SourceAppUserModelId) ? session.SourceAppUserModelId : "Unknown Source";
            if (mediaProperties != null && !string.IsNullOrEmpty(mediaProperties.Title))
            {
                 if (!name.Equals(mediaProperties.Title, StringComparison.OrdinalIgnoreCase))
                    name += $" ({mediaProperties.Title})";
            }
            else if (name == "Unknown Source")
            {
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
            if (sender is DispatcherTimer timer)
            {
                timer.Stop();
                Debug.WriteLine("Debounce timer elapsed. Calling UpdatePresenceFromSession...");
                await UpdatePresenceFromSession();
            }
        };
    }

    private void RequestPresenceUpdateDebounced()
    {
         if (_presenceUpdateDebounceTimer == null) return;

        _presenceUpdateDebounceTimer.Stop();
        _presenceUpdateDebounceTimer.Start();
        Debug.WriteLine($"Requested presence update (debounced for {DebounceMilliseconds}ms)");
    }

    private void MediaPropertiesChangedHandler(GlobalSystemMediaTransportControlsSession? sender, MediaPropertiesChangedEventArgs? args)
    {
        if (sender != null && _currentSession != null && sender.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
        {
            Debug.WriteLine($"MediaPropertiesChangedHandler: Properties changed for {sender.SourceAppUserModelId}");
            RequestPresenceUpdateDebounced();
        }
    }

    private void PlaybackInfoChangedHandler(GlobalSystemMediaTransportControlsSession? sender, PlaybackInfoChangedEventArgs? args)
    {
        if (sender != null && _currentSession != null && sender.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
        {
            Debug.WriteLine($"PlaybackInfoChangedHandler: Playback info changed for {sender.SourceAppUserModelId}");
            RequestPresenceUpdateDebounced();
        }
    }

    private async Task UpdatePresenceFromSession()
    {
        if (!_isRpcConnected || _client == null || !_client.IsInitialized || _mediaStateManager == null)
        {
            Debug.WriteLine($"UpdatePresenceFromSession skipped: Preconditions not met (RPC Connected: {_isRpcConnected}, Client Valid: {_client != null && _client.IsInitialized}, StateManager Valid: {_mediaStateManager != null})");
            return;
        }

        if (_currentSession == null)
        {
            Debug.WriteLine("UpdatePresenceFromSession: Clearing presence because current session is null");
            _client.ClearPresence();
            UpdateStatusText("No media source selected or active.");
            return;
        }

        Debug.WriteLine($"UpdatePresenceFromSession started for session: {_currentSession.SourceAppUserModelId}");

        try
        {
            RichPresence? newPresence = await _mediaStateManager.BuildRichPresenceAsync(_currentSession);

            if (newPresence == null)
            {
                Debug.WriteLine("UpdatePresenceFromSession: BuildRichPresenceAsync returned null");
                if (_mediaStateManager.CurrentState.IsUnknown)
                {
                    _client.ClearPresence();
                    UpdateStatusText("Waiting for media info...");
                }
                return;
            }

            if (_mediaStateManager.ShouldUpdatePresence(newPresence))
            {
                Debug.WriteLine("Presence data changed. Calling _client.SetPresence...");
                _client.SetPresence(newPresence);
                Debug.WriteLine("SetPresence called successfully");
                
                // Обновляем статус на главной странице
                UpdateStatusText($"{newPresence.Details} - {newPresence.State}");
            }
            else
            {
                Debug.WriteLine("Presence data hasn't changed. Skipping SetPresence call");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"--- Full Exception Details ---");
            Debug.WriteLine($"Error updating presence: {ex}");
            UpdateStatusText($"Error updating presence: {ex.Message}");
        }
    }

    private void UpdateStatusText(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = text;
                Debug.WriteLine($"UI Status updated: {text}");
            }
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _client?.Dispose();
        _sessionManager = null; // Явно освобождаем ресурсы SMTC
        _sharedHttpClient?.Dispose();
        // Закрываем приложение полностью
        Application.Current.Shutdown();
    }

    private void SwitchToSession(GlobalSystemMediaTransportControlsSession? newSession)
    {
        var previousSessionId = _currentSession?.SourceAppUserModelId ?? "null";
        var newSessionId = newSession?.SourceAppUserModelId ?? "null";

        if (previousSessionId == newSessionId) {
            Debug.WriteLine($"SwitchToSession called. Current: {previousSessionId}, New: {newSessionId}. Session is the same.");
            if (_mediaStateManager?.SelectedSourceAppId != newSessionId)
            {
                 Debug.WriteLine($"SwitchToSession [SYNC]: Session is same, but StateManager had different ID ('{_mediaStateManager?.SelectedSourceAppId}'). Syncing to '{newSessionId}'");
                 if (_mediaStateManager != null)
                 {
                    _mediaStateManager.SelectedSourceAppId = newSessionId;
                 }
                 RequestPresenceUpdateDebounced(); 
            }
            return;
        }

        Debug.WriteLine($"SwitchToSession called. Current: {previousSessionId}, New: {newSessionId}. Switching...");

        if (_currentSession != null)
        {
            Debug.WriteLine($"SwitchToSession: Unsubscribing from events for {previousSessionId}");
            _currentSession.MediaPropertiesChanged -= MediaPropertiesChangedHandler;
            _currentSession.PlaybackInfoChanged -= PlaybackInfoChangedHandler;
        }

        _currentSession = newSession;
        if (_mediaStateManager != null)
        {
            _mediaStateManager.SelectedSourceAppId = newSessionId;
        }

        if (_currentSession != null)
        {
            Debug.WriteLine($"SwitchToSession: Subscribing to events for {newSessionId}");
            _currentSession.MediaPropertiesChanged += MediaPropertiesChangedHandler;
            _currentSession.PlaybackInfoChanged += PlaybackInfoChangedHandler;
            Debug.WriteLine($"SwitchToSession: Set StateManager.SelectedSourceAppId to {newSessionId}");

            if (_isRpcConnected)
            {
                Debug.WriteLine("SwitchToSession: Requesting presence update for the new session...");
                RequestPresenceUpdateDebounced();
            }
            Debug.WriteLine($"Switched to session: {newSessionId}");
        }
        else // newSession is null
        {
            Debug.WriteLine("SwitchToSession: Clearing current session (newSession is null)");
            if (_isRpcConnected && _client != null && _client.IsInitialized)
            {
                _client.ClearPresence();
                 Debug.WriteLine("SwitchToSession: Cleared Discord presence.");
            }
            UpdateStatusText("No media source selected or active.");
        }
    }

    private void SessionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (_isComboBoxUpdate) // Игнорируем изменения во время заполнения списка
        {
            Debug.WriteLine($"SessionComboBox_SelectionChanged skipped: _isComboBoxUpdate={_isComboBoxUpdate}");
            return;
        }

        var selectedItem = SessionComboBox.SelectedItem as ComboBoxItem;
        Debug.WriteLine($"SessionComboBox_SelectionChanged triggered. Selected Item: '{selectedItem?.Content ?? "null"}', Index: {SessionComboBox.SelectedIndex}");

        if (selectedItem?.Tag is GlobalSystemMediaTransportControlsSession selectedSession)
        {
            Debug.WriteLine($"SessionComboBox_SelectionChanged: User selected session: {selectedSession.SourceAppUserModelId}");
            SwitchToSession(selectedSession); // Переключаемся на выбранную сессию
        }
        else if (SessionComboBox.SelectedIndex == -1 || selectedItem?.Content?.ToString() == "No media sources detected" || selectedItem?.Content?.ToString() == "Error loading sources")
        {
            Debug.WriteLine($"SessionComboBox_SelectionChanged: Selected placeholder or invalid item. Clearing session.");
            SwitchToSession(null); // Очищаем сессию
        }
        else
        {
            Debug.WriteLine($"SessionComboBox_SelectionChanged: Selected item has no valid session tag. Item: {selectedItem?.Content}. Doing nothing.");
            // Ничего не делаем, если выбран некорректный элемент (маловероятно)
        }

        UpdateButtonStates();
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("--- ConnectButton_Click called ---");
        InitializeDiscord();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("--- DisconnectButton_Click called ---");
        DisconnectDiscord();
    }

    private void DisconnectDiscord()
    {
        Debug.WriteLine("--- DisconnectDiscord called ---");
        try { Debug.WriteLine(Environment.StackTrace); } catch {}

        _client?.ClearPresence();
        _client?.Dispose();
        _client = null;
        _isRpcConnected = false;
        UpdateStatusText("Disconnected from Discord.");
        UpdateButtonStates();
        Debug.WriteLine("--- DisconnectDiscord finished ---");
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
            
            // Блокируем выбор источника после подключения
            SessionComboBox.IsEnabled = !_isRpcConnected;
            
            Debug.WriteLine($"UpdateButtonStates: sourceSelected={sourceSelected}, _isRpcConnected={_isRpcConnected}, canConnect={canConnect}, canDisconnect={canDisconnect}, SessionComboBox.IsEnabled={SessionComboBox.IsEnabled}");
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
            
        Debug.WriteLine($"Theme changed to: {newTheme}");
    }

    private void DevelopersHyperlink_Click(object sender, RoutedEventArgs e)
    {
        AboutWindow aboutWindow = new AboutWindow();
        aboutWindow.ShowDialog(); // Открываем как модальное окно
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
}