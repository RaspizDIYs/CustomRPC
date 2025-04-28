using System;
using System.Threading.Tasks;
using System.Windows;
using CustomMediaRPC.Views;
using Velopack;
using Velopack.Sources;

// Добавляем атрибут для Squirrel
[assembly: System.Reflection.AssemblyMetadata("SquirrelAwareVersion", "1")]

namespace CustomMediaRPC;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static UpdateManager? UpdateManager { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            VelopackApp.Build().Run();

            UpdateManager = new UpdateManager(new GithubSource("https://github.com/RaspizDIYs/CustomRPC", null, false));

            await CheckForUpdates();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка на старте: {ex.Message}");
        }

        var mainWindow = new Views.MainWindow();
        mainWindow.Show();
    }

    private async Task CheckForUpdates()
    {
        if (UpdateManager == null || !UpdateManager.IsInstalled)
        {
            Console.WriteLine("Пропуск проверки обновлений (приложение не установлено или менеджер не доступен).");
            return;
        }

        try
        {
            Console.WriteLine($"Текущая версия: {UpdateManager.CurrentVersion}");
            Console.WriteLine("Проверка обновлений...");

            var updateInfo = await UpdateManager.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                Console.WriteLine("Нет доступных обновлений.");
                return;
            }

            var latestVersion = updateInfo.TargetFullRelease.Version;
            Console.WriteLine($"Доступна новая версия: {latestVersion}");

            MessageBoxResult result = MessageBox.Show(
                $"Доступна новая версия ({latestVersion}). Хотите скачать и установить её сейчас?",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Console.WriteLine("Скачивание обновлений...");
                    await UpdateManager.DownloadUpdatesAsync(updateInfo, p => Console.WriteLine($"Загрузка: {p}%"));

                    Console.WriteLine("Применение обновлений и перезапуск...");
                    UpdateManager.ApplyUpdatesAndRestart(updateInfo);
                }
                catch (Exception downloadEx)
                {
                    Console.WriteLine($"Ошибка при скачивании/применении обновления: {downloadEx.Message}");
                    MessageBox.Show($"Ошибка при скачивании/применении обновления: {downloadEx.Message}", "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при проверке обновлений: {ex.Message}");
        }
    }
}