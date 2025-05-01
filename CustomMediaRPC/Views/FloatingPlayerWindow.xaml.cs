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

namespace CustomMediaRPC.Views
{
    public partial class FloatingPlayerWindow : Window
    {
        // События для кнопок управления
        public event EventHandler? PreviousRequested;
        public event EventHandler? PlayPauseToggleRequested;
        public event EventHandler? NextRequested;

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
                this.DragMove();
        }

        // Обновленный метод для обновления содержимого плеера
        public async void UpdateContent(string title, string artist, MediaPlaybackStatus status, IRandomAccessStreamReference? thumbnail = null, TimeSpan? currentPosition = null, TimeSpan? totalDuration = null)
        {
            TitleTextBlock.Text = title ?? "Unknown Title";
            ArtistTextBlock.Text = artist ?? "Unknown Artist";

            // Обновляем иконку Play/Pause
            PlayPauseIcon.Symbol = status == MediaPlaybackStatus.Playing ? SymbolRegular.Pause24 : SymbolRegular.Play24;

            // Загрузка обложки из потока или установка дефолтной
            BitmapImage bitmap = new BitmapImage();
            try
            {
                if (thumbnail != null)
                {
                    using (var stream = await thumbnail.OpenReadAsync()) // Открываем поток асинхронно
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        // Используем AsStreamForRead() для совместимости с WPF
                        bitmap.StreamSource = stream.AsStreamForRead(); 
                        bitmap.EndInit();
                        Debug.WriteLine("Successfully loaded cover art from SMTC thumbnail stream.");
                    }
                }
                else
                {
                    // Ставим обложку по умолчанию, если миниатюры нет
                    bitmap.BeginInit();
                    bitmap.UriSource = DefaultCoverUri;
                    bitmap.EndInit();
                    Debug.WriteLine("SMTC thumbnail is null, using default cover.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading cover art from SMTC thumbnail: {ex.Message}");
                // Ставим обложку по умолчанию при ошибке
                bitmap.BeginInit();
                bitmap.UriSource = DefaultCoverUri;
                bitmap.EndInit();
            }
            CoverArtImage.Source = bitmap; // Устанавливаем загруженную (или дефолтную) картинку

            // --- Логирование перед обновлением ProgressBar ---
            Debug.WriteLine($"[FloatingPlayer] UpdateContent - Received Times: Current={currentPosition}, Total={totalDuration}, Status={status}");

            // Обработка времени и управление таймером
            if (totalDuration.HasValue && totalDuration.Value > TimeSpan.Zero)
            {
                _totalDuration = totalDuration.Value;
                _lastKnownPosition = currentPosition ?? TimeSpan.Zero;
                _lastPositionUpdateTime = DateTime.UtcNow;

                TrackProgressBar.Maximum = _totalDuration.TotalSeconds;
                TrackProgressBar.Value = _lastKnownPosition.TotalSeconds;
                TrackProgressBar.Visibility = Visibility.Visible;

                if (status == MediaPlaybackStatus.Playing)
                {
                    _progressTimer?.Start();
                    Debug.WriteLine("[FloatingPlayer] Progress timer started.");
                }
                else
                {
                    _progressTimer?.Stop();
                    Debug.WriteLine("[FloatingPlayer] Progress timer stopped.");
                    // При паузе/остановке значение уже установлено правильно
                }
            }
            else
            {
                // Скрыть ProgressBar и остановить таймер, если длительность неизвестна
                TrackProgressBar.Visibility = Visibility.Collapsed;
                TrackProgressBar.Value = 0; 
                TrackProgressBar.Maximum = 100; // Сброс на всякий случай
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
    }
} 