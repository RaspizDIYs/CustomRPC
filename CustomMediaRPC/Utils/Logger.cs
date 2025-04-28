using System;
using System.Diagnostics;

namespace CustomMediaRPC.Utils;

public static class Logger
{
    public static void LogError(string message, Exception? ex = null)
    {
        var logMessage = ex == null ? message : $"{message}\nException: {ex}";
        Debug.WriteLine($"[ERROR] {logMessage}");
        // TODO: Добавить запись в файл или отправку в систему логирования
    }
    
    public static void LogInfo(string message)
    {
        Debug.WriteLine($"[INFO] {message}");
    }
    
    public static void LogDebug(string message)
    {
        #if DEBUG
        Debug.WriteLine($"[DEBUG] {message}");
        #endif
    }
    
    public static void LogWarning(string message)
    {
        Debug.WriteLine($"[WARNING] {message}");
    }
} 