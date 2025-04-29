using System;
using System.Diagnostics;

namespace CustomMediaRPC.Utils;

public static class DebugLogger
{
    // Формат времени можно настроить
    private const string TimeFormat = "HH:mm:ss.fff"; 

    public static void Log(string message)
    {
#if DEBUG
        // Выводим в Debug Output только в Debug сборке
        Debug.WriteLine($"[{DateTime.Now.ToString(TimeFormat)}] {message}");
#endif
        // Можно добавить логирование в файл здесь, если потребуется
        // Console.WriteLine($"[{DateTime.Now.ToString(TimeFormat)}] {message}");
    }
    
    // Перегрузка для удобства логирования исключений
    public static void Log(string message, Exception ex)
    {
#if DEBUG
        Debug.WriteLine($"[{DateTime.Now.ToString(TimeFormat)}] {message}\nException: {ex}");
#endif
        // Console.WriteLine($"[{DateTime.Now.ToString(TimeFormat)}] {message}\nException: {ex}");
    }
} 