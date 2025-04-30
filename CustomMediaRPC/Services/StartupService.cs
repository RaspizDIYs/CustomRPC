using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;

namespace CustomMediaRPC.Services
{
    public static class StartupService // Делаем статическим для простоты
    {
        // Имя ключа в реестре, которое будет использоваться для нашего приложения
        private const string AppName = "CustomMediaRPC"; 
        private static readonly string? AppPath = Assembly.GetExecutingAssembly().Location; // Путь к .exe

        // Путь к ключу реестра автозагрузки для текущего пользователя
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static void SetStartup(bool enable)
        {
            if (string.IsNullOrEmpty(AppPath))
            {
                 Debug.WriteLine("StartupService: Не удалось получить путь к исполняемому файлу.");
                 // TODO: Показать ошибку пользователю?
                 return;
            }
            
            // Используем блок try-catch для обработки возможных ошибок доступа к реестру
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                    {
                        Debug.WriteLine($"StartupService: Не удалось открыть ключ реестра '{RegistryKeyPath}'.");
                        // TODO: Показать ошибку?
                        return;
                    }

                    if (enable)
                    {
                        // Добавляем или обновляем значение в реестре
                        key.SetValue(AppName, AppPath);
                        Debug.WriteLine($"StartupService: Приложение '{AppName}' добавлено в автозагрузку. Путь: {AppPath}");
                    }
                    else
                    {
                        // Удаляем значение из реестра, если оно существует
                        if (key.GetValue(AppName) != null)
                        {
                            key.DeleteValue(AppName, false); // false - не выбрасывать исключение, если ключ не найден
                            Debug.WriteLine($"StartupService: Приложение '{AppName}' удалено из автозагрузки.");
                        }
                        else
                        {
                             Debug.WriteLine($"StartupService: Приложение '{AppName}' не найдено в автозагрузке, удаление не требуется.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"StartupService: Ошибка при работе с реестром: {ex.Message}");
                 // TODO: Показать ошибку пользователю?
            }
        }

        // Метод для проверки, включена ли автозагрузка
        public static bool IsStartupEnabled()
        {
             if (string.IsNullOrEmpty(AppPath)) return false;
             
             try
             {
                 using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false)) // false - только чтение
                 {
                     if (key == null) return false;
                     
                     object? value = key.GetValue(AppName);
                     // Проверяем, что значение существует и совпадает с текущим путем (на случай перемещения файла)
                     return value != null && value.ToString().Equals(AppPath, StringComparison.OrdinalIgnoreCase);
                 }
             }
             catch (Exception ex)
             {
                  Debug.WriteLine($"StartupService: Ошибка при проверке статуса автозагрузки: {ex.Message}");
                  return false;
             }
        }
    }
} 