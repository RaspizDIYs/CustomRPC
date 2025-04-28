using System;
using System.Linq;
using System.Text;

namespace CustomMediaRPC.Utils;

public static class StringUtils
{
    /// <summary>
    /// Обрезает строку до указанного максимального количества байт в кодировке UTF-8,
    /// стараясь не разрывать символы.
    /// </summary>
    /// <param name="str">Исходная строка.</param>
    /// <param name="maxBytes">Максимальное количество байт.</param>
    /// <param name="suffix">Суффикс, добавляемый к обрезанной строке (его байты учитываются в лимите).</param>
    /// <returns>Обрезанная строка.</returns>
    public static string TruncateStringByBytesUtf8(string? str, int maxBytes, string suffix = "...")
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;

        // Простая проверка, чтобы избежать лишних вычислений
        if (str.Length * 4 < maxBytes) // Максимум 4 байта на символ UTF-8
        {
            int quickBytes = Encoding.UTF8.GetByteCount(str);
            if (quickBytes <= maxBytes) return str;
        }

        int suffixBytes = Encoding.UTF8.GetByteCount(suffix);
        if (maxBytes <= suffixBytes) return suffix; // Недостаточно места даже для суффикса

        int targetBytes = maxBytes - suffixBytes;
        var sb = new StringBuilder();
        int currentBytes = 0;

        foreach (char c in str)
        {
            // Используем EncoderFallback для безопасного подсчета байт символа
            int charBytes = Encoding.UTF8.GetByteCount(new[] { c });

            if (currentBytes + charBytes <= targetBytes)
            {
                sb.Append(c);
                currentBytes += charBytes;
            }
            else
            {
                break; // Превышен лимит байт
            }
        }

        // Добавляем суффикс, если строка была обрезана
        // Проверяем, изменилась ли длина StringBuilder по сравнению с оригинальной строкой
        // (хотя сравнение байт было бы точнее, но это проще)
        if (sb.Length < str.Length)
        {
             return sb.ToString() + suffix;
        }
        else
        {
             return sb.ToString(); // Строка поместилась полностью
        }
    }
} 