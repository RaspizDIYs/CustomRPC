using System.Windows;
using Wpf.Ui.Controls; // Используем FluentWindow
using CustomMediaRPC.Models; // Добавляем using для AppSettings
using CustomMediaRPC.Services; // Добавляем using для сервиса плеера
using System.Threading.Tasks; // Добавляем using для Task

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
    }
} 