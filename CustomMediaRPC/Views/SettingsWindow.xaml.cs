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
using System.Linq; // For LINQ
using System.Globalization; // For CultureInfo

namespace CustomMediaRPC.Views
{
    public partial class SettingsWindow : FluentWindow
    {
        private readonly AppSettings _appSettings; // Храним ссылку на настройки
        private readonly SettingsService _settingsService; // Добавляем поле для SettingsService
        private bool _isInitializing = true; // Flag to prevent premature event firing

        // Принимаем AppSettings и SettingsService в конструкторе
        public SettingsWindow(AppSettings settings, SettingsService settingsService)
        {
            InitializeComponent();
            _appSettings = settings; // Сохраняем ссылку
            _settingsService = settingsService; // Сохраняем ссылку
            DataContext = _appSettings; // Set DataContext for Binding
            PopulateLanguageComboBox();
            LoadSettings();
            _isInitializing = false; // Initialization complete
        }

        private void PopulateLanguageComboBox()
        {
            LanguageComboBox.ItemsSource = LocalizationManager.SupportedLanguages;
            LanguageComboBox.DisplayMemberPath = "Value"; // Show display name (e.g., "Русский")
            LanguageComboBox.SelectedValuePath = "Key"; // Use culture code (e.g., "ru-RU") as value
        }

        private void LoadSettings()
        {
            // Загрузка настроек из _appSettings
            EnableFloatingPlayerCheckBox.IsChecked = _appSettings.EnableFloatingPlayer;
            AlwaysOnTopCheckBox.IsChecked = _appSettings.PlayerAlwaysOnTop;
            LaunchOnStartupCheckBox.IsChecked = StartupService.IsStartupEnabled();
            // No need to set _appSettings.LaunchOnStartup here, it's handled by the checkbox event
            // AutoUpdateCheckBox and SilentAutoUpdateCheckBox are bound via DataContext
            
            // Select the current language in the ComboBox
            LanguageComboBox.SelectedValue = _appSettings.Language;

            // Hide restart required message initially
            RestartRequiredTextBlock.Visibility = Visibility.Collapsed;
        }

        private void TitleBar_CloseClicked(TitleBar sender, RoutedEventArgs args)
        {
            SaveSettings(); // Save on close
            Close();
        }

        private void SaveSettings()
        {
            // No need to manually save most settings if using TwoWay binding
            // We only need to ensure the language is saved if it changed
            // AppSettingsService?.SaveSettings(_appSettings); // Assuming a service to save settings
            // Or directly save if no service:
            _ = _settingsService.SaveSettingsAsync(); // Используем инъектированный сервис
        }

        // Обработчики событий Changed для чекбоксов
        private void EnableFloatingPlayerCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null || _isInitializing) return;
            bool isChecked = EnableFloatingPlayerCheckBox.IsChecked ?? false;
            _appSettings.EnableFloatingPlayer = isChecked;
            FloatingPlayerService.Instance.SetVisibility(isChecked);
            System.Diagnostics.Debug.WriteLine($"Setting: EnableFloatingPlayer set to {isChecked}");
        }

        private void AlwaysOnTopCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null || _isInitializing) return;
            bool isChecked = AlwaysOnTopCheckBox.IsChecked ?? false;
             _appSettings.PlayerAlwaysOnTop = isChecked;
             FloatingPlayerService.Instance.SetAlwaysOnTop(isChecked);
             System.Diagnostics.Debug.WriteLine($"Setting: PlayerAlwaysOnTop set to {isChecked}");
        }

        private void LaunchOnStartupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_appSettings == null || _isInitializing) return;
            bool isChecked = LaunchOnStartupCheckBox.IsChecked ?? false;
            _appSettings.LaunchOnStartup = isChecked;
            System.Diagnostics.Debug.WriteLine($"Setting: LaunchOnStartup set to {isChecked}");

            // Вызываем логику регистрации/отмены автозагрузки
            StartupService.SetStartup(isChecked);
        }

        private void AutoUpdateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
             if (_appSettings == null || _isInitializing || DataContext == null) return;
            // Binding handles the _appSettings update
            System.Diagnostics.Debug.WriteLine($"Setting: AutoCheckForUpdates set to {_appSettings.AutoCheckForUpdates}");
        }

        // Обработчик для нового чекбокса тихого обновления
        private void SilentAutoUpdateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
             if (_appSettings == null || _isInitializing || DataContext == null) return;
            // Binding handles the _appSettings update
             System.Diagnostics.Debug.WriteLine($"Setting: SilentAutoUpdates set to {_appSettings.SilentAutoUpdates}");
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || LanguageComboBox.SelectedValue == null) return;

            string selectedLanguageCode = LanguageComboBox.SelectedValue.ToString() ?? "ru-RU";

            // Check if the language actually changed
            if (_appSettings.Language != selectedLanguageCode)
            {
                _appSettings.Language = selectedLanguageCode;
                RestartRequiredTextBlock.Visibility = Visibility.Visible; // Show restart required message
                System.Diagnostics.Debug.WriteLine($"Setting: Language changed to {selectedLanguageCode}. Restart required.");
                // No need to call LocalizationManager.SetCulture here, it's handled on startup
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            //SaveSettings(); // Incorrect synchronous call
            await _settingsService.SaveSettingsAsync(); // Используем инъектированный сервис
            base.OnClosed(e);
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