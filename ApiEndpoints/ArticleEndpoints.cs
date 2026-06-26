using Rssary.DataStore;

namespace Rssary.ApiEndpoints;

/// <summary>
/// 文章 + 统计 + 搜索 + 健康检查 API
/// </summary>
public static class ArticleEndpoints
{
    public static void MapArticleEndpoints(this WebApplication app)
    {
        // 文章列表（分页）
        app.MapGet("/api/articles", (SiteQueries site, BlogStore blogs,
            int page = 1, int pageSize = 20) =>
        {
            var (articles, totalCount) = site.GetPaged(page, pageSize);
            var blogMap = blogs.GetApproved().ToDictionary(b => b.Id);

            return Results.Ok(new
            {
                Articles = articles.Select(a => new
                {
                    a.Id, a.Title, a.Link, a.Description, a.PublishedAt, a.Author,
                    Blog = blogMap.TryGetValue(a.BlogId, out var b) ? new { b.Id, b.Name } : null
                }),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(15)));

        // 搜索文章
        app.MapGet("/api/search", (string q, SiteQueries site, BlogStore blogs) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { Error = "Query parameter 'q' is required" });

            var searchResult = site.Search(q);
            var blogMap = blogs.GetApproved().ToDictionary(b => b.Id);

            return Results.Ok(new
            {
                Query = q,
                Count = searchResult.Count,
                Articles = searchResult.Articles.Select(a => new
                {
                    a.Id, a.Title, a.Link, a.Description, a.PublishedAt, a.Author,
                    Blog = blogMap.TryGetValue(a.BlogId, out var b) ? new { b.Id, b.Name } : null
                })
            });
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).SetVaryByQuery(["q"]));

        // 统计信息
        app.MapGet("/api/stats", (SiteQueries site) =>
        {
            var stats = site.GetStats();
            return Results.Ok(new
            {
                BlogCount = stats.BlogCount,
                ArticleCount = stats.ArticleCount,
                LastUpdated = stats.LastUpdate
            });
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(30)));

        // ===== 健康检查（含各组件状态） =====
        app.MapGet("/api/health", (HttpContext context) =>
        {
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var storeDir = Path.Combine(env.ContentRootPath, "AppStorage");
            var configExists = File.Exists(Path.Combine(storeDir, "config.toml"));
            var blogsExists = File.Exists(Path.Combine(storeDir, "blogs.toml"));
            var articlesDir = Path.Combine(storeDir, "Articles");
            var articlesDirExists = Directory.Exists(articlesDir);

            return Results.Ok(new
            {
                Status = configExists ? "Healthy" : "Unhealthy",
                Timestamp = DateTime.UtcNow,
                Components = new
                {
                    Config = configExists ? "ok" : "missing",
                    Blogs = blogsExists ? "ok" : "empty",
                    ArticlesDir = articlesDirExists ? "ok" : "not_created"
                }
            });
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(30)));
    }
}
