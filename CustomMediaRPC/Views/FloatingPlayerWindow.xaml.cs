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

        public FloatingPlayerWindow()
        {
            InitializeComponent();
            // Устанавливаем обложку по умолчанию при инициализации
            CoverArtImage.Source = new BitmapImage(DefaultCoverUri);
        }

        // Позволяет перетаскивать окно
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        // Обновленный метод для обновления содержимого плеера
        public async void UpdateContent(string title, string artist, MediaPlaybackStatus status, IRandomAccessStreamReference? thumbnail = null)
        {
            TitleTextBlock.Text = string.IsNullOrEmpty(title) ? "Unknown Title" : title;
            ArtistTextBlock.Text = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist;

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