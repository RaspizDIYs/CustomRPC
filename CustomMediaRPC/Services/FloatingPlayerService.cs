using System.Windows;
using CustomMediaRPC.Views;
using CustomMediaRPC.Models;
using System.Diagnostics;
using System;
using Windows.Storage.Streams;

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
                 _playerWindow.Closed -= PlayerWindow_Closed;
                 _playerWindow = null; // Сбрасываем ссылку
                 Debug.WriteLine("FloatingPlayerWindow closed and events unsubscribed.");
            }
        }

        // Обработчики событий от окна плеера, транслирующие их наружу
        private void OnPlayerPreviousRequested(object? sender, EventArgs e) => PreviousRequested?.Invoke(this, e);
        private void OnPlayerPlayPauseToggleRequested(object? sender, EventArgs e) => PlayPauseToggleRequested?.Invoke(this, e);
        private void OnPlayerNextRequested(object? sender, EventArgs e) => NextRequested?.Invoke(this, e);

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

        // Обновляем метод, чтобы принимать IRandomAccessStreamReference
        public void UpdateContent(string title, string artist, MediaPlaybackStatus status, IRandomAccessStreamReference? thumbnail = null, TimeSpan? currentPosition = null, TimeSpan? totalDuration = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                 if (_playerWindow != null && _playerWindow.IsLoaded && _playerWindow.IsVisible)
                 {
                    // Передаем миниатюру вместо URL
                    _playerWindow.UpdateContent(title, artist, status, thumbnail, currentPosition, totalDuration);
                    Debug.WriteLine($"FloatingPlayerWindow content updated: {title} - {artist} ({status}), Thumbnail: {thumbnail != null}, Time: {currentPosition}/{totalDuration}");
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