using System;
using System.Threading.Tasks;
using System.Windows;
using CustomMediaRPC.Models;
using CustomMediaRPC.Services;
using CustomMediaRPC.Views;
using Velopack;
using Velopack.Sources;
using CustomMediaRPC.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using System.Threading;

namespace CustomMediaRPC;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static UpdateManager? UpdateManager { get; private set; }
    public static SettingsService SettingsService { get; private set; } = new();
    private static IHost? AppHost { get; set; }
    private Mutex? _mutex;
    private const string MutexName = "Global\\CustomMediaRPC";

    public App()
    {
        // Initialize LocalizationManager first
        var settings = LoadSettings(); // Need to load settings to get the language
        LocalizationManager.Initialize(settings.Language);

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<AppSettings>(settings); // Use the loaded settings
                services.AddSingleton<SpotifyService>();
                services.AddSingleton<MediaStateManager>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<SettingsWindow>();
                services.AddSingleton<AboutWindow>();
                services.AddSingleton<ChangelogWindow>();
                services.AddSingleton<FloatingPlayerWindow>();
            })
            .Build();

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
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("Another instance of Custom Media RPC is already running.", "Application Already Running", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        await AppHost!.StartAsync();

        var startupWindow = AppHost.Services.GetRequiredService<MainWindow>();
        var settings = AppHost.Services.GetRequiredService<AppSettings>();

        if (settings.StartMinimized)
        {
            startupWindow.WindowState = WindowState.Minimized;
            startupWindow.Hide(); // Hide the main window if starting minimized
        }
        else
        {
            startupWindow.Show();
        }

        // Check for first launch after initializing services
        if (settings.FirstLaunch)
        {
            var aboutWindow = AppHost.Services.GetRequiredService<AboutWindow>();
            aboutWindow.Show();
            // Optionally show settings too or guide user
            // var settingsWindow = AppHost.Services.GetRequiredService<SettingsWindow>();
            // settingsWindow.Show();
        }

        // Загружаем настройки
        await SettingsService.LoadSettingsAsync();
        SettingsService.RegisterSettingsSaveOnPropertyChange();

        try
        {
            // Инициализируем UpdateManager ТОЛЬКО при обычном запуске
            // (т.к. VelopackApp.Run() уже обработал служебные запуски в Main)
            UpdateManager = new UpdateManager(new GithubSource("https://github.com/RaspizDIYs/CustomRPC", null, false));
            
            if (UpdateManager?.IsInstalled == true) // Проверяем, установлено ли приложение и инициализирован ли менеджер
            {
                // Проверяем настройку автообновления
                if (SettingsService.CurrentSettings.AutoCheckForUpdates)
                {
                    // Запускаем проверку обновлений
                    await CheckForUpdates();
                }
                else
                {
                    Console.WriteLine("Автоматическая проверка обновлений отключена в настройках.");
                }
            } else
            {
                Console.WriteLine("Приложение не установлено через Velopack или UpdateManager null, пропускаем проверку обновлений.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка инициализации UpdateManager или проверки обновлений: {ex.Message}");
        }
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

            // Проверяем, включено ли тихое обновление
            if (SettingsService.CurrentSettings.SilentAutoUpdates)
            {
                Console.WriteLine("Тихое обновление включено. Начинаем скачивание в фоне...");
                try
                {
                    // Просто скачиваем, Velopack применит при следующем запуске
                    await UpdateManager.DownloadUpdatesAsync(updateInfo, p => Console.WriteLine($"Авто-загрузка: {p}%"));
                    Console.WriteLine("Обновление скачано. Будет применено при следующем запуске.");
                }
                catch (Exception downloadEx)
                {
                    Console.WriteLine($"Ошибка при фоновом скачивании обновления: {downloadEx.Message}");
                    // Возможно, стоит показать ошибку пользователю?
                    // MessageBox.Show($"Не удалось скачать обновление в фоне: {downloadEx.Message}", "Ошибка автообновления", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else // Обычное обновление с запросом
            {
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при проверке обновлений: {ex.Message}");
        }
    }

    private static AppSettings LoadSettings()
    {
        AppSettings? settings = null;
        bool firstLaunch = false;
        try
        {
            if (File.Exists(Constants.SettingsPath))
            {
                var json = File.ReadAllText(Constants.SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch (Exception ex)
        {
            // Handle error (e.g., log it, show a message)
            MessageBox.Show($"Error loading settings: {ex.Message}\nUsing default settings.", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (settings == null)
        {
            settings = new AppSettings();
            firstLaunch = true; // Mark as first launch if settings didn't exist or failed to load
            // Save initial default settings
            SaveSettingsStatic(settings);
        }
        settings.FirstLaunch = firstLaunch;
        return settings;
    }

    // Static version for saving before DI container is built
    private static void SaveSettingsStatic(AppSettings settings)
    {
         try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            Directory.CreateDirectory(Path.GetDirectoryName(Constants.SettingsPath)!);
            File.WriteAllText(Constants.SettingsPath, json);
        }
        catch (Exception ex)
        {
             MessageBox.Show($"Error saving initial settings: {ex.Message}", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost!.StopAsync();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // Handle unhandled exceptions
    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception details
        // Logger.LogError(e.Exception, "An unhandled exception occurred");

        MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                        "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);

        // Prevent default WPF crash behavior
        e.Handled = true;

        // Optionally, try to gracefully shut down
        // Shutdown();
    }
}