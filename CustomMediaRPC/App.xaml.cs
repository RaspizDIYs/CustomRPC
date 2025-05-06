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
    private static IHost? AppHost { get; set; }
    private Mutex? _mutex;
    private const string MutexName = "Global\\CustomMediaRPC";

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Критическая ошибка при запуске приложения: {ex.Message}", "Ошибка Запуска", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public App()
    {
        var settings = LoadSettings();
        LocalizationManager.Initialize(settings.Language);

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<AppSettings>(settings);
                services.AddSingleton<SpotifyService>();
                services.AddSingleton<DeezerService>();
                services.AddSingleton<MediaStateManager>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<SettingsWindow>();
                services.AddSingleton<AboutWindow>();
                services.AddSingleton<ChangelogWindow>();
                services.AddSingleton<FloatingPlayerWindow>();
            })
            .Build();

        DispatcherUnhandledException += Application_DispatcherUnhandledException;
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

        if (AppHost == null) 
        {
             MessageBox.Show("Critical error: AppHost is null before starting.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
             Shutdown();
             return;
        }
        await AppHost.StartAsync();

        var startupWindow = AppHost.Services.GetRequiredService<MainWindow>();
        var settings = AppHost.Services.GetRequiredService<AppSettings>();

        if (settings.StartMinimized)
        {
            startupWindow.WindowState = WindowState.Minimized;
            startupWindow.Hide();
        }
        else
        {
            startupWindow.Show();
        }

        if (settings.FirstLaunch)
        {
            var aboutWindow = AppHost.Services.GetRequiredService<AboutWindow>();
            aboutWindow.Show();
        }

        var settingsService = AppHost.Services.GetRequiredService<SettingsService>();

        await settingsService.LoadSettingsAsync();

        try
        {
            UpdateManager = new UpdateManager(new GithubSource("https://github.com/RaspizDIYs/CustomRPC", null, false));
            
            if (UpdateManager?.IsInstalled == true)
            {
                var currentSettings = AppHost.Services.GetRequiredService<AppSettings>(); 
                if (currentSettings == null)
                {
                     Console.WriteLine("Ошибка: currentSettings is null after GetRequiredService<AppSettings>().");
                }
                else if (currentSettings.AutoCheckForUpdates)
                {
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

            var settingsService = AppHost.Services.GetRequiredService<SettingsService>(); 
            if (settingsService != null)
            {
                var currentSettings = AppHost.Services.GetRequiredService<AppSettings>();
                if (currentSettings.SilentAutoUpdates)
                {
                    Console.WriteLine("Тихое обновление включено. Начинаем скачивание в фоне...");
                    try
                    {
                        await UpdateManager.DownloadUpdatesAsync(updateInfo, p => Console.WriteLine($"Авто-загрузка: {p}%"));
                        Console.WriteLine("Обновление скачано. Будет применено при следующем запуске.");
                    }
                    catch (Exception downloadEx)
                    {
                        Console.WriteLine($"Ошибка при фоновом скачивании обновления: {downloadEx.Message}");
                    }
                }
                else
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
            MessageBox.Show($"Error loading settings: {ex.Message}\nUsing default settings.", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (settings == null)
        {
            settings = new AppSettings();
            firstLaunch = true;
            SaveSettingsStatic(settings);
        }
        settings.FirstLaunch = firstLaunch;
        return settings;
    }

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
        if (AppHost != null)
        {
            var settingsService = AppHost.Services.GetService<SettingsService>();
            if (settingsService != null)
            {
                DebugLogger.Log("Application exiting. Explicitly saving settings...");
                await settingsService.SaveSettingsAsync();
                DebugLogger.Log("Settings saved explicitly on exit.");
            }
            
            await AppHost.StopAsync();
        }
        
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                        "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }
}