using System;
using System.Windows;
using System.Windows.Input; // Для MouseButtonEventArgs
using System.Windows.Media.Imaging; // Для BitmapImage
using System.Diagnostics;
using Wpf.Ui.Controls; // Для SymbolIcon
using CustomMediaRPC.Models; // Для MediaPlaybackStatus
using Windows.Storage.Streams; // Для IRandomAccessStreamReference
using System.Runtime.InteropServices.WindowsRuntime; // Для AsStreamForRead()
using System.IO; // Добавляем для AsStreamForRead()
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Threading; // Добавляем для DispatcherTimer
using Windows.Media.Control; // Add this for PlaybackStatus
using CustomMediaRPC.Services; // Add this for LocalizationManager

namespace CustomMediaRPC.Views
{
    public partial class FloatingPlayerWindow : Window
    {
        // События для кнопок управления
        public event EventHandler? PreviousRequested;
        public event EventHandler? PlayPauseToggleRequested;
        public event EventHandler? NextRequested;
        public event EventHandler? HideRequested;

        // Можно добавить URI для обложки по умолчанию
        private static readonly Uri DefaultCoverUri = new Uri("pack://application:,,,/favicon.ico"); 

        // Таймер для симуляции прогресса
        private DispatcherTimer? _progressTimer;
        private TimeSpan _lastKnownPosition = TimeSpan.Zero;
        private TimeSpan _totalDuration = TimeSpan.Zero;
        private DateTime _lastPositionUpdateTime = DateTime.MinValue;

        public FloatingPlayerWindow()
        {
            InitializeComponent();
            // Устанавливаем обложку по умолчанию при инициализации
            CoverArtImage.Source = new BitmapImage(DefaultCoverUri);
            InitializeProgressTimer();
            // Initialize with default content (null stream reference)
            UpdateContent("No track playing", "", GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped, null, 0, 100); 
        }

        private void InitializeProgressTimer()
        {
            _progressTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250) // Обновляем 4 раза в секунду
            };
            _progressTimer.Tick += ProgressTimer_Tick;
        }

        // Позволяет перетаскивать окно
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try 
                {
                    DragMove();
                }
                 catch (InvalidOperationException) 
                 { 
                     // Can happen if the window is clicked very quickly after opening/closing
                 }
            }
        }

        // Update method signature to accept IRandomAccessStreamReference?
        public void UpdateContent(string? title, string? artist, GlobalSystemMediaTransportControlsSessionPlaybackStatus status, IRandomAccessStreamReference? thumbnailRef, double currentPosition, double maxPosition)
        {
             Debug.WriteLine($"[FloatingPlayer] UpdateContent called: Title='{title}', Artist='{artist}', Status='{status}', ThumbnailRef is null: {thumbnailRef == null}, Position={currentPosition}/{maxPosition}");
             if (!Dispatcher.CheckAccess())
            {
                // Invoke the internal method with the new signature
                Dispatcher.Invoke(() => UpdateContentInternal(title, artist, status, thumbnailRef, currentPosition, maxPosition));
            }
            else
            {
                // Call the internal method directly with the new signature
                UpdateContentInternal(title, artist, status, thumbnailRef, currentPosition, maxPosition);
            }
        }
        
        // Update internal method signature and implement async image loading
        private async void UpdateContentInternal(string? title, string? artist, GlobalSystemMediaTransportControlsSessionPlaybackStatus status, IRandomAccessStreamReference? thumbnailRef, double currentPosition, double maxPosition)
        {
            TitleTextBlock.Text = string.IsNullOrEmpty(title) ? LocalizationManager.GetString("UnknownTitle") : title;
            ArtistTextBlock.Text = string.IsNullOrEmpty(artist) ? LocalizationManager.GetString("UnknownArtist") : artist;

            // Update Play/Pause button icon and tooltip
            bool isPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // PlayPauseButton.Content = isPlaying ? "⏸" : "▶"; // Temporary text icons - REMOVED
            // Correctly update the Symbol property of the nested icon
            PlayPauseIcon.Symbol = isPlaying ? Wpf.Ui.Controls.SymbolRegular.Pause20 : Wpf.Ui.Controls.SymbolRegular.Play20;
            PlayPauseButton.ToolTip = isPlaying
                ? LocalizationManager.GetString("FloatingPlayer_PauseButton_ToolTip")
                : LocalizationManager.GetString("FloatingPlayer_PlayButton_ToolTip");

            // Asynchronously load cover art from stream reference or set default
            BitmapImage bitmap = new BitmapImage();
            try
            {
                if (thumbnailRef != null)
                {
                    using (var stream = await thumbnailRef.OpenReadAsync())
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream.AsStreamForRead(); // Use AsStreamForRead for WPF compatibility
                        bitmap.EndInit();
                        Debug.WriteLine("Successfully loaded cover art from SMTC thumbnail stream.");
                    }
                }
                else
                {
                    bitmap.BeginInit();
                    bitmap.UriSource = DefaultCoverUri;
                    bitmap.EndInit();
                    Debug.WriteLine("SMTC thumbnailRef is null, using default cover.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading cover art from SMTC thumbnailRef: {ex.Message}");
                bitmap.BeginInit();
                bitmap.UriSource = DefaultCoverUri;
                bitmap.EndInit();
            }
            CoverArtImage.Source = bitmap; // Set the loaded or default image

            // --- Logging before updating ProgressBar ---
            Debug.WriteLine($"[FloatingPlayer] UpdateContentInternal - Received Times: Current={currentPosition}, Total={maxPosition}, Status={status}");

            // Handle time and timer management
            if (maxPosition > 0 && currentPosition >= 0 && currentPosition <= maxPosition)
            {
                _totalDuration = TimeSpan.FromSeconds(maxPosition);
                _lastKnownPosition = TimeSpan.FromSeconds(currentPosition);
                _lastPositionUpdateTime = DateTime.UtcNow;

                TrackProgressBar.Maximum = _totalDuration.TotalSeconds;
                TrackProgressBar.Value = _lastKnownPosition.TotalSeconds;
                TrackProgressBar.Visibility = Visibility.Visible;

                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    _progressTimer?.Start();
                    Debug.WriteLine("[FloatingPlayer] Progress timer started.");
                }
                else
                {
                    _progressTimer?.Stop();
                    Debug.WriteLine("[FloatingPlayer] Progress timer stopped.");
                }
            }
            else
            {
                TrackProgressBar.Visibility = Visibility.Collapsed;
                TrackProgressBar.Value = 0; 
                TrackProgressBar.Maximum = 100;
                _progressTimer?.Stop();
                _totalDuration = TimeSpan.Zero;
                 Debug.WriteLine("[FloatingPlayer] Progress timer stopped (no duration).");
            }
        }
        
        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            if (_totalDuration > TimeSpan.Zero)
            {
                var elapsedSinceUpdate = DateTime.UtcNow - _lastPositionUpdateTime;
                var estimatedPosition = _lastKnownPosition + elapsedSinceUpdate;

                // Ограничиваем максимальное значение
                if (estimatedPosition >= _totalDuration)
                {
                    estimatedPosition = _totalDuration;
                    _progressTimer?.Stop(); // Останавливаем таймер в конце трека
                    Debug.WriteLine("[FloatingPlayer] Progress timer stopped (end of track reached).");
                }

                TrackProgressBar.Value = estimatedPosition.TotalSeconds;
                // Debug.WriteLine($"[FloatingPlayer Timer Tick] Estimated Pos: {estimatedPosition.TotalSeconds}/{_totalDuration.TotalSeconds}");
            }
            else
            {
                _progressTimer?.Stop(); // На всякий случай, если длительность вдруг стала некорректной
                 Debug.WriteLine("[FloatingPlayer] Progress timer stopped (tick with no duration).");
            }
        }

        // Метод для установки свойства AlwaysOnTop
        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
            this.Topmost = alwaysOnTop;
        }

        // Обработчики нажатия кнопок
        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            PreviousRequested?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine("Previous button clicked");
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPauseToggleRequested?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine("Play/Pause button clicked");
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NextRequested?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine("Next button clicked");
        }

        // Обработчик клика по кнопке скрыть
        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Hide button clicked");
            HideRequested?.Invoke(this, EventArgs.Empty); // Вызываем событие
        }
    }
} 