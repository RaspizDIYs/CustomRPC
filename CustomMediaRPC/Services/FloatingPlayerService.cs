using System.Windows;
using CustomMediaRPC.Views;
using CustomMediaRPC.Models;
using System.Diagnostics;
using System;
using Windows.Storage.Streams;
using Windows.Media.Control;

namespace CustomMediaRPC.Services
{
    public class FloatingPlayerService
    {
        private FloatingPlayerWindow? _playerWindow;
        private AppSettings? _appSettings;

        // События, которые сервис будет транслировать наружу
        public event EventHandler? PreviousRequested;
        public event EventHandler? PlayPauseToggleRequested;
        public event EventHandler? NextRequested;

        // Singleton pattern (простой вариант)
        private static readonly FloatingPlayerService _instance = new FloatingPlayerService();
        public static FloatingPlayerService Instance => _instance;

        private FloatingPlayerService() 
        { 
            // Приватный конструктор для Singleton
        }

        public void Initialize(AppSettings settings)
        {
             _appSettings = settings;
            if (_appSettings.EnableFloatingPlayer)
            {
                EnsurePlayerWindowExists(); // Создаст окно и подпишется на события
                SetVisibility(true);
                SetAlwaysOnTop(_appSettings.PlayerAlwaysOnTop);
            }
        }

        private void EnsurePlayerWindowExists()
        {
            if (_playerWindow == null || Application.Current.Dispatcher.Invoke(() => !_playerWindow.IsLoaded))
            {
                // Создаем окно в UI потоке
                Application.Current.Dispatcher.Invoke(() => 
                {
                    _playerWindow = new FloatingPlayerWindow();
                    // Подписываемся на события окна
                    _playerWindow.PreviousRequested += OnPlayerPreviousRequested;
                    _playerWindow.PlayPauseToggleRequested += OnPlayerPlayPauseToggleRequested;
                    _playerWindow.NextRequested += OnPlayerNextRequested;
                    _playerWindow.Closed += PlayerWindow_Closed; // Обрабатываем закрытие
                    _playerWindow.HideRequested += PlayerWindow_HideRequested; // Подписка на новое событие
                    Debug.WriteLine("FloatingPlayerWindow instance created and events subscribed.");
                });
            }
        }
        
        private void PlayerWindow_Closed(object? sender, EventArgs e)
        {
            // Отписываемся от событий при закрытии окна
            if (_playerWindow != null)
            {
                 _playerWindow.PreviousRequested -= OnPlayerPreviousRequested;
                 _playerWindow.PlayPauseToggleRequested -= OnPlayerPlayPauseToggleRequested;
                 _playerWindow.NextRequested -= OnPlayerNextRequested;
                 _playerWindow.HideRequested -= PlayerWindow_HideRequested; // Отписка от нового события
                 _playerWindow.Closed -= PlayerWindow_Closed;
                 _playerWindow = null; // Сбрасываем ссылку
                 Debug.WriteLine("FloatingPlayerWindow closed and events unsubscribed.");
            }
        }

        // Обработчики событий от окна плеера, транслирующие их наружу
        private void OnPlayerPreviousRequested(object? sender, EventArgs e) => PreviousRequested?.Invoke(this, e);
        private void OnPlayerPlayPauseToggleRequested(object? sender, EventArgs e) => PlayPauseToggleRequested?.Invoke(this, e);
        private void OnPlayerNextRequested(object? sender, EventArgs e) => NextRequested?.Invoke(this, e);

        // Обработчик для нового события скрытия
        private void PlayerWindow_HideRequested(object? sender, EventArgs e)
        {
            // HidePlayer(); // Просто вызываем наш метод скрытия - НЕПРАВИЛЬНО для кнопки
            SetVisibility(false); // Вызываем метод, который меняет настройку и скрывает
            Debug.WriteLine("HideRequested event received from player window. Calling SetVisibility(false).");
        }

        public void SetVisibility(bool isVisible)
        {
            if (_appSettings == null) 
            {
                Debug.WriteLine("SetVisibility skipped: AppSettings not initialized.");
                return;
            }
            _appSettings.EnableFloatingPlayer = isVisible; 

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isVisible)
                {
                    EnsurePlayerWindowExists();
                    _playerWindow?.Show();
                    _playerWindow?.Activate(); // Активируем окно
                    Debug.WriteLine("FloatingPlayerWindow shown and activated (initial content will come from regular update).");

                    // --- УБРАНА ЛОГИКА ПОЛУЧЕНИЯ И ПРИМЕНЕНИЯ НАЧАЛЬНОГО СОСТОЯНИЯ ---
                    // var initialState = MediaStateManager.Instance.GetCurrentState();
                    // if (_playerWindow != null && initialState != null) { ... }
                    // else if (_playerWindow != null) { ... }
                    // ---------------------------------------------------------------
                }
                else
                {
                    // Просто скрываем, не закрываем, чтобы сохранить состояние
                    // Используем HidePlayer(), чтобы не менять _appSettings.EnableFloatingPlayer
                    // _playerWindow?.Hide(); // Неправильно, нужно использовать HidePlayer()
                    HidePlayer(); // Правильный вызов для скрытия без изменения настройки
                    Debug.WriteLine("FloatingPlayerWindow hidden via SetVisibility(false) -> HidePlayer().");
                }
            });
        }

        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
             if (_appSettings == null) 
             {
                Debug.WriteLine("SetAlwaysOnTop skipped: AppSettings not initialized.");
                return;
             }

            Application.Current.Dispatcher.Invoke(() =>
            {
                 if (_playerWindow != null && _playerWindow.IsLoaded)
                 {
                     _playerWindow.SetAlwaysOnTop(alwaysOnTop);
                     Debug.WriteLine($"FloatingPlayerWindow AlwaysOnTop set to: {alwaysOnTop}");
                 }
                 else
                 {
                    Debug.WriteLine("SetAlwaysOnTop skipped: Player window not loaded or null.");
                 }
            });
        }

        // Helper method to convert custom status to system status
        private GlobalSystemMediaTransportControlsSessionPlaybackStatus ConvertStatus(MediaPlaybackStatus customStatus)
        {
            return customStatus switch
            {
                MediaPlaybackStatus.Playing => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                MediaPlaybackStatus.Paused => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
                MediaPlaybackStatus.Stopped => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped,
                MediaPlaybackStatus.Unknown => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped, 
                _ => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped
            };
        }

        // Update the method signature to accept IRandomAccessStreamReference
        public void UpdateContent(string title, string artist, MediaPlaybackStatus status, IRandomAccessStreamReference? thumbnail = null, TimeSpan? currentPosition = null, TimeSpan? totalDuration = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                 if (_playerWindow != null && _playerWindow.IsLoaded && _playerWindow.IsVisible)
                 {
                    // Convert status and extract total seconds, providing defaults
                    var systemStatus = ConvertStatus(status);
                    double currentSec = currentPosition?.TotalSeconds ?? 0;
                    double totalSec = totalDuration?.TotalSeconds ?? 0;

                    // Pass thumbnail stream reference directly, use converted status and seconds
                    _playerWindow.UpdateContent(title, artist, systemStatus, thumbnail, currentSec, totalSec);
                    Debug.WriteLine($"FloatingPlayerWindow content updated via service: {title} - {artist} ({systemStatus}), Thumbnail: {thumbnail != null}, Time: {currentSec}/{totalSec}");
                 }
                 else
                 {
                    Debug.WriteLine("UpdateContent skipped: Player window not loaded, null, or hidden.");
                 }
            });
        }
        
        // Новый метод для скрытия плеера без изменения настроек
        public void HidePlayer()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _playerWindow?.Hide();
                Debug.WriteLine("FloatingPlayerWindow hidden via HidePlayer().");
            });
        }

        public void Shutdown()
        {
             Application.Current.Dispatcher.Invoke(() =>
             {
                // Отписываемся от событий перед закрытием
                PlayerWindow_Closed(null, EventArgs.Empty); // Используем наш метод отписки
             });
        }
    }
} 