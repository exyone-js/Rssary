using System.Security.Cryptography;
using System.Text;
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

    private const string DefaultReviewKeyHash = "";
    private const string DefaultSiteTitle = "Rssary";
    private const string DefaultSiteDescription = "RSS/Feed Aggregator";
    private const string DefaultSiteLanguage = "en-US";

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

    public string GetReviewKeyHash()
    {
        var table = ReadTable();
        var hash = TomlHelper.GetString(table, "review_key_hash", DefaultReviewKeyHash);
        
        // 向后兼容：自动迁移旧版 review_key 到 review_key_hash
        if (string.IsNullOrEmpty(hash))
        {
            var oldKey = TomlHelper.GetStringOrNull(table, "review_key");
            if (!string.IsNullOrEmpty(oldKey))
            {
                hash = HashKey(oldKey);
                // 写入新格式并移除旧格式
                WriteTable(t =>
                {
                    t["review_key_hash"] = hash;
                    t.Remove("review_key");
                });
            }
        }
        
        return hash;
    }
    public string GetSiteTitle() => TomlHelper.GetString(ReadTable(), "site_title", DefaultSiteTitle);
    public string GetSiteDescription() => TomlHelper.GetString(ReadTable(), "site_description", DefaultSiteDescription);
    public string GetSiteLanguage() => TomlHelper.GetString(ReadTable(), "site_language", DefaultSiteLanguage);
    public string GetHeadInjection() => TomlHelper.GetStringOrNull(ReadTable(), "head_injection") ?? "";
    public string GetBodyStartInjection() => TomlHelper.GetStringOrNull(ReadTable(), "body_start_injection") ?? "";
    public string GetBodyEndInjection() => TomlHelper.GetStringOrNull(ReadTable(), "body_end_injection") ?? "";

    /// <summary>
    /// 校验管理密钥是否匹配
    /// </summary>
    public bool ValidateReviewKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var hash = HashKey(input);
        return hash == GetReviewKeyHash();
    }

    // ===== 写入方法 =====

    /// <summary>
    /// 设置管理密钥（存储 SHA256 哈希，不通文明文存储）
    /// </summary>
    public void SetReviewKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 6)
            throw new ArgumentException("审核密钥至少需要6位");

        // 跳过哈希存储已有哈希值的情况（用于初始化时直接写已有哈希）
        // 正常情况下应该是明文密钥
        var isAlreadyHashed = key.Length == 64 && key.All(c => char.IsAsciiHexDigit(c));
        var value = isAlreadyHashed ? key : HashKey(key);

        WriteTable(t =>
        {
            t.Remove("review_key"); // 移除旧明文 key（如果存在）
            t["review_key_hash"] = value;
        });
        _logger.LogInformation("Review key hash updated");
    }

    public void SetSiteTitle(string title)
    {
        WriteTable(t => t["site_title"] = title ?? DefaultSiteTitle);
    }

    public void SetSiteDescription(string desc)
    {
        WriteTable(t => t["site_description"] = desc ?? DefaultSiteDescription);
    }

    public void SetSiteLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            throw new ArgumentException("语言不能为空");
        WriteTable(t => t["site_language"] = lang);
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
            ReviewKeyHash = TomlHelper.GetString(table, "review_key_hash", DefaultReviewKeyHash),
            SiteTitle = TomlHelper.GetString(table, "site_title", DefaultSiteTitle),
            SiteDescription = TomlHelper.GetString(table, "site_description", DefaultSiteDescription),
            SiteLanguage = TomlHelper.GetString(table, "site_language", DefaultSiteLanguage)
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
            SiteLanguage = TomlHelper.GetString(table, "site_language", DefaultSiteLanguage),
            HeadInjection = TomlHelper.GetStringOrNull(table, "head_injection") ?? "",
            BodyStartInjection = TomlHelper.GetStringOrNull(table, "body_start_injection") ?? "",
            BodyEndInjection = TomlHelper.GetStringOrNull(table, "body_end_injection") ?? ""
        };
    }

    // ===== 哈希工具 =====

    private static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
