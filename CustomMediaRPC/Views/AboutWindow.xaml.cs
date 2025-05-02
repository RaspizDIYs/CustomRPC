using System.Windows;
using System.Diagnostics;
using Wpf.Ui.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System;
using System.Reflection; // Added for Assembly
using CustomMediaRPC.Services; // Added for LocalizationManager

namespace CustomMediaRPC.Views;

/// <summary>
/// Логика взаимодействия для AboutWindow.xaml
/// </summary>
public partial class AboutWindow : FluentWindow
{
    private bool _isClosing = false; // Флаг для предотвращения повторного закрытия

    public AboutWindow()
    {
        InitializeComponent();
        // Устанавливаем владельца окна, чтобы оно открывалось по центру родительского
        Owner = Application.Current.MainWindow;
        // Принудительно применяем текущую тему приложения
        // (Хотя FluentWindow должен делать это сам, для надежности)
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);

        // Подписываемся на событие потери фокуса
        Deactivated += AboutWindow_Deactivated;

        // Set version text using localized string format
        /* // Commented out to remove version display
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "?.?.?";
            // Use LocalizationManager to format the string
            VersionText.Text = string.Format(LocalizationManager.GetString("AboutWindow_Version"), versionString);
        }
        catch (Exception ex)
        {
            // Handle error, maybe show a simpler version string or log
            VersionText.Text = string.Format(LocalizationManager.GetString("AboutWindow_Version"), "err");
            Debug.WriteLine($"Error getting application version for About window: {ex.Message}"); 
        }
        */
    }

    // Обработчик события потери фокуса - закрываем окно
    private void AboutWindow_Deactivated(object? sender, EventArgs e)
    {
        // Добавим проверку, чтобы окно не закрылось, если открыто дочернее (например, MessageBox)
        // И проверяем флаг, что окно не закрывается уже
        if (!_isClosing && this.OwnedWindows.Count == 0) 
        {
            _isClosing = true; // Ставим флаг
            Deactivated -= AboutWindow_Deactivated; // Отписываемся от события!
            
            // Планируем закрытие окна через Dispatcher, чтобы выйти из текущего контекста события
            Dispatcher.BeginInvoke(new Action(() => 
            {
                if (!this.IsLoaded) return; // Доп. проверка, если окно успело выгрузиться
                try { this.Close(); }
                catch (InvalidOperationException) { /* Игнорируем, если все же не успели */ }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void DonateButtonAbout_Click(object sender, RoutedEventArgs e)
    {
        string donationUrl = "https://www.donationalerts.com/r/mejaikin";
        try
        {
            Process.Start(new ProcessStartInfo(donationUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open donate URL: {ex.Message}");
            System.Windows.MessageBox.Show($"Could not open the donation link: {ex.Message}", 
                                           "Error", 
                                           System.Windows.MessageBoxButton.OK,
                                           System.Windows.MessageBoxImage.Error);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
        e.Handled = true;
    }
} 