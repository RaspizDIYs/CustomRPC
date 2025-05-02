using System.Windows;
using Wpf.Ui.Controls; // Для FluentWindow
using System.Collections.Generic; // Для List
using System.Linq; // Для Take()

namespace CustomMediaRPC.Views
{
    /// <summary>
    /// Логика взаимодействия для ChangelogWindow.xaml
    /// </summary>
    public partial class ChangelogWindow : FluentWindow
    {
        // Конструктор для отображения релизов
        public ChangelogWindow(List<MainWindow.GitHubRelease> releases)
        {
            InitializeComponent();
            ErrorTextBlock.Visibility = Visibility.Collapsed; // Скрываем текст ошибки
            ReleasesItemsControl.ItemsSource = releases; // Устанавливаем источник данных
            ApplyTheme();
        }

        // Конструктор для отображения ошибки
        public ChangelogWindow(string errorMessage)
        {
            InitializeComponent();
            ReleasesItemsControl.Visibility = Visibility.Collapsed; // Скрываем список релизов
            ErrorTextBlock.Text = errorMessage; // Показываем текст ошибки
            ErrorTextBlock.Visibility = Visibility.Visible;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            // Применяем текущую тему
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
        }

        // Обработчик для открытия ссылки в браузере
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening hyperlink: {ex.Message}");
                // Можно показать MessageBox, если нужно
            }
            e.Handled = true;
        }
    }
} 