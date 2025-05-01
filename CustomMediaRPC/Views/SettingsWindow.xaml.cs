using System.Windows;
using Wpf.Ui.Controls; // Используем FluentWindow
using CustomMediaRPC.Models; // Добавляем using для AppSettings
using CustomMediaRPC.Services; // Добавляем using для сервиса плеера
using System.Threading.Tasks; // Добавляем using для Task
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;

namespace CustomMediaRPC.Views
{
    public partial class SettingsWindow : FluentWindow
    {
        private AppSettings _appSettings; // Храним ссылку на настройки

        // Принимаем AppSettings в конструкторе
        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _appSettings = settings; // Сохраняем ссылку
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Загрузка настроек из _appSettings
            EnableFloatingPlayerCheckBox.IsChecked = _appSettings.EnableFloatingPlayer;
            AlwaysOnTopCheckBox.IsChecked = _appSettings.PlayerAlwaysOnTop;
            LaunchOnStartupCheckBox.IsChecked = StartupService.IsStartupEnabled();
            _appSettings.LaunchOnStartup = LaunchOnStartupCheckBox.IsChecked ?? false;
            AutoUpdateCheckBox.IsChecked = _appSettings.AutoCheckForUpdates;
            SilentAutoUpdateCheckBox.IsChecked = _appSettings.SilentAutoUpdates;
        }

        private void TitleBar_CloseClicked(TitleBar sender, RoutedEventArgs args)
        {
            Close();
        }
        
        // Обработчики событий Changed для чекбоксов
        private void EnableFloatingPlayerCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null) return;
            bool isChecked = EnableFloatingPlayerCheckBox.IsChecked ?? false;
            _appSettings.EnableFloatingPlayer = isChecked;
            FloatingPlayerService.Instance.SetVisibility(isChecked);
            System.Diagnostics.Debug.WriteLine($"Setting: EnableFloatingPlayer set to {isChecked}");
        }

        private void AlwaysOnTopCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null) return;
            bool isChecked = AlwaysOnTopCheckBox.IsChecked ?? false;
             _appSettings.PlayerAlwaysOnTop = isChecked;
             FloatingPlayerService.Instance.SetAlwaysOnTop(isChecked);
             System.Diagnostics.Debug.WriteLine($"Setting: PlayerAlwaysOnTop set to {isChecked}");
        }

        private void LaunchOnStartupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null) return;
            bool isChecked = LaunchOnStartupCheckBox.IsChecked ?? false;
            _appSettings.LaunchOnStartup = isChecked;
            System.Diagnostics.Debug.WriteLine($"Setting: LaunchOnStartup set to {isChecked}");
            
            // Вызываем логику регистрации/отмены автозагрузки
            StartupService.SetStartup(isChecked);
        }

        private void AutoUpdateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null) return;
            bool isChecked = AutoUpdateCheckBox.IsChecked ?? false;
            _appSettings.AutoCheckForUpdates = isChecked;
            System.Diagnostics.Debug.WriteLine($"Setting: AutoCheckForUpdates set to {isChecked}");
            // Сама проверка будет происходить в другом месте при старте приложения
        }

        // Обработчик для нового чекбокса тихого обновления
        private void SilentAutoUpdateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null) return;
            bool isChecked = SilentAutoUpdateCheckBox.IsChecked ?? false;
            _appSettings.SilentAutoUpdates = isChecked;
            System.Diagnostics.Debug.WriteLine($"Setting: SilentAutoUpdates set to {isChecked}");
        }

        // Обработчик нажатия кнопки ручной проверки обновлений
        private async void ManualCheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var updateManager = App.UpdateManager; // Получаем менеджер из App

            if (updateManager == null || !updateManager.IsInstalled)
            {
                 System.Windows.MessageBox.Show("Update check is not available (app not installed via Velopack or UpdateManager failed to initialize).", 
                                       "Update Check", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Опционально: Отключить кнопку на время проверки
            ManualCheckUpdateButton.IsEnabled = false;
            ManualCheckUpdateButton.Content = "Checking...";

            try
            {
                System.Diagnostics.Debug.WriteLine("Manual update check started...");
                var updateInfo = await updateManager.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    System.Diagnostics.Debug.WriteLine("No updates found.");
                    System.Windows.MessageBox.Show("You are using the latest version.", 
                                           "Update Check", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    var latestVersion = updateInfo.TargetFullRelease.Version;
                    System.Diagnostics.Debug.WriteLine($"Update found: {latestVersion}");
                    System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                        $"A new version ({latestVersion}) is available. Do you want to download and install it now?",
                        "Update Available",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Information);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        try
                        {
                            ManualCheckUpdateButton.Content = "Downloading...";
                            System.Diagnostics.Debug.WriteLine("Downloading updates...");
                            // Отображение прогресса пока не реализовано для ручной проверки
                            await updateManager.DownloadUpdatesAsync(updateInfo);

                            ManualCheckUpdateButton.Content = "Applying...";
                            System.Diagnostics.Debug.WriteLine("Applying updates and restarting...");
                            updateManager.ApplyUpdatesAndRestart(updateInfo);
                            // Приложение перезапустится здесь, дальнейший код не выполнится
                        }
                        catch (Exception downloadEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error downloading/applying update: {downloadEx.Message}");
                            System.Windows.MessageBox.Show($"Error downloading or applying the update: {downloadEx.Message}", 
                                                   "Update Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for updates: {ex.Message}");
                System.Windows.MessageBox.Show($"An error occurred while checking for updates: {ex.Message}", 
                                       "Update Check Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                // Включаем кнопку обратно
                ManualCheckUpdateButton.IsEnabled = true;
                ManualCheckUpdateButton.Content = "Check for Updates Now";
            }
        }
    }
} 