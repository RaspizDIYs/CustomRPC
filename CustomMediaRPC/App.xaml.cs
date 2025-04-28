using System;
using System.Threading.Tasks;
using System.Windows;
using CustomMediaRPC.Views;
using Velopack;
using Velopack.Sources;

namespace CustomMediaRPC;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static UpdateManager? UpdateManager { get; private set; }

    public App()
    {
        try
        {
            VelopackApp.Build().Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Критическая ошибка инициализации Velopack: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            // Возможно, стоит завершить приложение?
            // Shutdown();
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Инициализируем UpdateManager ТОЛЬКО при обычном запуске
            // (т.к. VelopackApp.Run() уже обработал служебные запуски в Main)
            UpdateManager = new UpdateManager(new GithubSource("https://github.com/RaspizDIYs/CustomRPC", null, false));
            
            if (UpdateManager?.IsInstalled == true) // Проверяем, установлено ли приложение и инициализирован ли менеджер
            {
                // Запускаем проверку обновлений
                await CheckForUpdates();
            } else
            {
                Console.WriteLine("Приложение не установлено через Velopack или UpdateManager null, пропускаем проверку обновлений.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка инициализации UpdateManager или проверки обновлений: {ex.Message}");
        }

        var mainWindow = new Views.MainWindow();
        mainWindow.Show();
    }

    private async Task CheckForUpdates()
    {
        if (UpdateManager == null)
        {
            Console.WriteLine("UpdateManager не инициализирован, пропуск проверки обновлений.");
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