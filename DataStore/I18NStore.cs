using Tomlyn;
using Tomlyn.Model;

namespace Rssary.DataStore;

/// <summary>
/// 国际化文案存储 —— 从 i18n.toml 读取多语言翻译
/// </summary>
public class I18NStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, TomlTable>? _cache;

    public I18NStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// 获取指定语言的完整嵌套翻译数据
    /// </summary>
    public Dictionary<string, object?>? GetLanguage(string lang)
    {
        var all = GetAll();
        return all.TryGetValue(lang, out var data) ? ToNestedDict(data) : null;
    }

    /// <summary>
    /// 获取全部原始数据（按语言）
    /// </summary>
    public Dictionary<string, TomlTable> GetAll()
    {
        if (_cache != null) return _cache;

        lock (_lock)
        {
            if (_cache != null) return _cache;

            if (!File.Exists(_filePath))
            {
                _cache = new Dictionary<string, TomlTable>();
                return _cache;
            }

            try
            {
                var text = File.ReadAllText(_filePath);
                var root = Toml.ToModel(text);
                var result = new Dictionary<string, TomlTable>();

                foreach (var key in root.Keys)
                {
                    if (root[key] is TomlTable langTable)
                    {
                        result[key] = langTable;
                    }
                }

                _cache = result;
            }
            catch
            {
                _cache = new Dictionary<string, TomlTable>();
            }

            return _cache;
        }
    }

    /// <summary>
    /// 获取可用语言列表（code + 原生显示名称）
    /// </summary>
    public List<LanguageEntry> GetAvailableLanguages()
    {
        var all = GetAll();
        var list = new List<LanguageEntry>();

        foreach (var code in all.Keys)
        {
            try
            {
                var ci = new System.Globalization.CultureInfo(code);
                // 取简写：如 "中文(中华人民共和国)" → "中文"，"English (United States)" → "English"
                var nativeName = ci.NativeName;
                var parenIdx = nativeName.IndexOf('(');
                var displayName = parenIdx > 0 ? nativeName[..parenIdx].Trim() : nativeName;
                list.Add(new LanguageEntry { Code = code, DisplayName = displayName });
            }
            catch
            {
                // 无法识别的语言代码，直接使用 code
                list.Add(new LanguageEntry { Code = code, DisplayName = code });
            }
        }

        return list;
    }

    /// <summary>
    /// 写入 i18n.toml
    /// </summary>
    public void SaveAll(Dictionary<string, TomlTable> data)
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Rssary 国际化翻译配置");
            sb.AppendLine("# 用户可在此文件自定义或修改所有前端文案");
            sb.AppendLine();

            foreach (var (lang, table) in data)
            {
                // Write all flattened sections for this language
                WriteSections(sb, table, lang);
                sb.AppendLine();
            }

            File.WriteAllText(_filePath, sb.ToString());
            _cache = data;
        }
    }

    /// <summary>
    /// 递归写出 TOML section 和键值对
    /// dotPath 是不带括号的路径，如 "zh-CN.nav"
    /// </summary>
    private static void WriteSections(System.Text.StringBuilder sb, TomlTable table, string dotPath)
    {
        // Write section header
        sb.AppendLine($"[{dotPath}]");
        sb.AppendLine();

        foreach (var key in table.Keys)
        {
            var val = table[key];
            if (val is TomlTable subTable)
            {
                // Recurse into sub-section with dot-path
                WriteSections(sb, subTable, $"{dotPath}.{key}");
            }
            else if (val is string s)
            {
                var escaped = TomlHelper.Escape(s);
                sb.AppendLine($"{key} = \"{escaped}\"");
            }
        }
    }

    /// <summary>
    /// 将 TomlTable 递归转为 Dictionary[string, object?] 用于 API 返回
    /// </summary>
    private static Dictionary<string, object?> ToNestedDict(TomlTable table)
    {
        var result = new Dictionary<string, object?>();
        foreach (var key in table.Keys)
        {
            var val = table[key];
            if (val is TomlTable subTable)
                result[key] = ToNestedDict(subTable);
            else
                result[key] = val?.ToString();
        }
        return result;
    }
}

public class LanguageEntry
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
