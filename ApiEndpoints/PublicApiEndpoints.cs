using Rssary.DataStore;

namespace Rssary.ApiEndpoints;

/// <summary>
/// 公开数据 API — 各类型数据映射到独立端点
/// </summary>
public static class PublicApiEndpoints
{
    public static void MapPublicApiEndpoints(this WebApplication app)
    {
        // ===== API 索引 =====
        app.MapGet("/api", (HttpContext context) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            return Results.Ok(new
            {
                Message = "Rssary 公开 API。各类型数据对应独立端点。",
                Endpoints = new
                {
                    SiteInfo = $"{baseUrl}/api/site",
                    Blogs = $"{baseUrl}/api/blogs",
                    Articles = $"{baseUrl}/api/articles?page=1&pageSize=20",
                    BlogArticles = $"{baseUrl}/api/blog/{{id}}/articles?page=1&pageSize=20",
                    Stats = $"{baseUrl}/api/stats",
                    Search = $"{baseUrl}/api/search?q=keyword",
                    RandomQuote = $"{baseUrl}/api/random-quote",
                    RandomBlogs = $"{baseUrl}/api/random-blogs?count=5",
                    Health = $"{baseUrl}/api/health",
                    RssFeed = $"{baseUrl}/feed",
                    SiteSettings = $"{baseUrl}/api/admin/site-settings",
                }
            });
        });

        // ===== 站点信息 =====
        app.MapGet("/api/site", (SiteQueries site, AppConfigManager config) =>
        {
            var stats = site.GetStats();
            return Results.Ok(new
            {
                Title = config.GetSiteTitle() ?? "Rssary",
                Description = config.GetSiteDescription() ?? "去中心化 RSS/Feed 整合平台",
                BlogCount = stats.BlogCount,
                ArticleCount = stats.ArticleCount,
                LastUpdated = stats.LastUpdate
            });
        }).CacheOutput("ApiPolicy");
    }
}
