using Tomlyn;
using Tomlyn.Model;

namespace Rssary.DataStore;

/// <summary>
/// TOML 字符串工具（转义、取值）
/// </summary>
internal static class TomlHelper
{
    public static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public static string GetString(TomlTable table, string key, string defaultValue = "")
        => table.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;

    public static string? GetStringOrNull(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value)) return null;
        var str = value?.ToString();
        return string.IsNullOrWhiteSpace(str) ? null : str;
    }
}
