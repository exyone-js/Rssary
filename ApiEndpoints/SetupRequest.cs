namespace Rssary.ApiEndpoints;

/// <summary>
/// 首次初始化配置请求体
/// </summary>
public class SetupRequest
{
    public string ReviewKey { get; set; } = "";
    public string? SiteTitle { get; set; }
    public string? SiteDescription { get; set; }
    public string? SiteLanguage { get; set; }
}
