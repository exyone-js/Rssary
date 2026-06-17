using Tomlyn;

namespace Rssary.DataStore;

/// <summary>
/// 应用配置管理 —— 读写 config.toml，支持运行时修改
/// </summary>
public class AppConfigManager
{
    private readonly string _configPath;
    private readonly object _fileLock = new();
    private readonly ILogger<AppConfigManager> _logger;

    private const string DefaultReviewKey = "B10gSw4rm_Exy0nE091710";
    private const string DefaultSiteTitle = "Rssary";
    private const string DefaultSiteDescription = "RSS/Feed 聚合平台";

    public AppConfigManager(string configPath, ILogger<AppConfigManager> logger)
    {
        _configPath = configPath;
        _logger = logger;
    }

    private Tomlyn.Model.TomlTable ReadTable()
    {
        if (!File.Exists(_configPath)) return new Tomlyn.Model.TomlTable();

        lock (_fileLock)
        {
            try
            {
                var text = File.ReadAllText(_configPath);
                return Toml.ToModel(text);
            }
            catch
            {
                return new Tomlyn.Model.TomlTable();
            }
        }
    }

    private void WriteTable(Action<Tomlyn.Model.TomlTable> modify)
    {
        lock (_fileLock)
        {
            var table = ReadTable();
            modify(table);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Rssary 站点配置");
            sb.AppendLine();
            foreach (var key in table.Keys.OrderBy(k => k))
            {
                var val = table[key];
                if (val is string s)
                {
                    var valStr = TomlHelper.Escape(s);
                    sb.AppendLine($"{key} = \"{valStr}\"");
                }
            }

            File.WriteAllText(_configPath, sb.ToString());
        }
    }

    // ===== 读取方法 =====

    public string GetReviewKey() => TomlHelper.GetString(ReadTable(), "review_key", DefaultReviewKey);
    public string GetSiteTitle() => TomlHelper.GetString(ReadTable(), "site_title", DefaultSiteTitle);
    public string GetSiteDescription() => TomlHelper.GetString(ReadTable(), "site_description", DefaultSiteDescription);
    public string GetHeadInjection() => TomlHelper.GetStringOrNull(ReadTable(), "head_injection") ?? "";
    public string GetBodyStartInjection() => TomlHelper.GetStringOrNull(ReadTable(), "body_start_injection") ?? "";
    public string GetBodyEndInjection() => TomlHelper.GetStringOrNull(ReadTable(), "body_end_injection") ?? "";

    // ===== 写入方法 =====

    public void SetReviewKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 6)
            throw new ArgumentException("审核密钥至少需要6位");
        WriteTable(t => t["review_key"] = key);
        _logger.LogInformation("Review key updated");
    }

    public void SetSiteTitle(string title)
    {
        WriteTable(t => t["site_title"] = title ?? DefaultSiteTitle);
    }

    public void SetSiteDescription(string desc)
    {
        WriteTable(t => t["site_description"] = desc ?? DefaultSiteDescription);
    }

    public void SetHeadInjection(string html)
    {
        WriteTable(t =>
        {
            if (string.IsNullOrWhiteSpace(html))
                t.Remove("head_injection");
            else
                t["head_injection"] = html;
        });
    }

    public void SetBodyStartInjection(string html)
    {
        WriteTable(t =>
        {
            if (string.IsNullOrWhiteSpace(html))
                t.Remove("body_start_injection");
            else
                t["body_start_injection"] = html;
        });
    }

    public void SetBodyEndInjection(string html)
    {
        WriteTable(t =>
        {
            if (string.IsNullOrWhiteSpace(html))
                t.Remove("body_end_injection");
            else
                t["body_end_injection"] = html;
        });
    }

    // ===== 批量 =====

    public object GetFullConfig()
    {
        var table = ReadTable();
        return new
        {
            ReviewKey = TomlHelper.GetString(table, "review_key", DefaultReviewKey),
            SiteTitle = TomlHelper.GetString(table, "site_title", DefaultSiteTitle),
            SiteDescription = TomlHelper.GetString(table, "site_description", DefaultSiteDescription)
        };
    }

    /// <summary>
    /// 公开的站点设置（包含注入内容，无需密钥）
    /// </summary>
    public object GetPublicSettings()
    {
        var table = ReadTable();
        return new
        {
            SiteTitle = TomlHelper.GetString(table, "site_title", DefaultSiteTitle),
            SiteDescription = TomlHelper.GetString(table, "site_description", DefaultSiteDescription),
            HeadInjection = TomlHelper.GetStringOrNull(table, "head_injection") ?? "",
            BodyStartInjection = TomlHelper.GetStringOrNull(table, "body_start_injection") ?? "",
            BodyEndInjection = TomlHelper.GetStringOrNull(table, "body_end_injection") ?? ""
        };
    }
}
