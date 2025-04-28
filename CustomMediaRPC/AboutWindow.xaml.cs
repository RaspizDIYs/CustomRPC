using System.Windows;
using System.Diagnostics;
using Wpf.Ui.Controls;

namespace CustomMediaRPC
{
    /// <summary>
    /// Логика взаимодействия для AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : FluentWindow
    {
        public AboutWindow()
        {
            InitializeComponent();
            // Устанавливаем владельца окна, чтобы оно открывалось по центру родительского
            Owner = Application.Current.MainWindow;
            // Принудительно применяем текущую тему приложения
            // (Хотя FluentWindow должен делать это сам, для надежности)
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
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
    }
} 