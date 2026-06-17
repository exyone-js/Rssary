using Rssary.DataStore;
using Rssary.DomainModels;

namespace Rssary.ApiEndpoints;

/// <summary>
/// 审核管理 + 站点管理 API
/// </summary>
public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        // ===== 审核验证 =====
        var review = app.MapGroup("/api/review");

        review.MapGet("/auth", (AppConfigManager cfg, string key) =>
        {
            return key == cfg.GetReviewKey()
                ? Results.Ok(new { Ok = true })
                : Results.Unauthorized();
        });

        review.MapGet("/pending", (AppConfigManager cfg, BlogStore blogs, string key) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            return Results.Ok(blogs.GetPending().Select(b => new
            {
                b.Id, b.Name, b.Url, b.RssUrl, b.Description, b.SubmittedAt
            }));
        });

        // 全部博客列表（不显示状态）
        review.MapGet("/all-blogs", (AppConfigManager cfg, BlogStore blogs, string key) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            return Results.Ok(blogs.GetAll().Select(b => new
            {
                b.Id, b.Name, b.Url, b.RssUrl, b.Description, b.SubmittedAt, b.ApprovedAt
            }));
        });

        review.MapPost("/approve", async (string blogId, string key, AppConfigManager cfg,
            BlogStore blogs, ArticleStore articles, RssReading.RssFeedReader reader) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            blogs.UpdateStatus(blogId, BlogStatus.Approved);

            var blog = blogs.GetById(blogId);
            if (blog != null)
            {
                var fetched = await reader.FetchArticlesAsync(blog);
                articles.AddNew(blogId, fetched);
            }
            return Results.Ok(new { Message = "博客已审核通过" });
        });

        review.MapPost("/reject", (string blogId, string key, AppConfigManager cfg, BlogStore blogs) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            blogs.UpdateStatus(blogId, BlogStatus.Rejected);
            return Results.Ok(new { Message = "博客已拒绝" });
        });

        review.MapPost("/resync", async (string blogId, string key, AppConfigManager cfg,
            BlogStore blogs, ArticleStore articles, RssReading.RssFeedReader reader) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            var blog = blogs.GetById(blogId);
            if (blog == null) return Results.NotFound();

            var fetched = await reader.FetchArticlesAsync(blog);
            articles.AddNew(blogId, fetched);
            return Results.Ok(new { Message = $"同步完成，获取 {fetched.Count} 篇文章", Count = fetched.Count });
        });

        review.MapPost("/delete", (string blogId, string key, AppConfigManager cfg, BlogStore blogs) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            blogs.Delete(blogId);
            return Results.Ok(new { Message = "博客已删除" });
        });

        // ===== 站点管理 =====
        var admin = app.MapGroup("/api/admin");

        admin.MapGet("/config", (AppConfigManager cfg, string key) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();
            return Results.Ok(cfg.GetFullConfig());
        });

        admin.MapPost("/config", (SiteConfigUpdate input, AppConfigManager cfg, string key) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();

            if (!string.IsNullOrEmpty(input.ReviewKey))
                cfg.SetReviewKey(input.ReviewKey);
            if (!string.IsNullOrEmpty(input.SiteTitle))
                cfg.SetSiteTitle(input.SiteTitle);
            if (input.SiteDescription != null)
                cfg.SetSiteDescription(input.SiteDescription);
            if (input.HeadInjection != null)
                cfg.SetHeadInjection(input.HeadInjection);
            if (input.BodyStartInjection != null)
                cfg.SetBodyStartInjection(input.BodyStartInjection);
            if (input.BodyEndInjection != null)
                cfg.SetBodyEndInjection(input.BodyEndInjection);

            return Results.Ok(new { Message = "配置已更新" });
        });

        // 站点设置（公开）—— 前端用它做注入
        admin.MapGet("/site-settings", (AppConfigManager cfg) =>
        {
            return Results.Ok(cfg.GetPublicSettings());
        });

        // 站点统计
        admin.MapGet("/stats", (AppConfigManager cfg, BlogStore blogs, SiteQueries site, string key) =>
        {
            if (key != cfg.GetReviewKey()) return Results.Unauthorized();

            var (blogCount, articleCount, lastUpdate) = site.GetStats();
            var all = blogs.GetAll();
            var pending = blogs.GetPending();

            return Results.Ok(new
            {
                TotalBlogs = all.Count,
                ApprovedBlogs = blogCount,
                PendingBlogs = pending.Count,
                RejectedBlogs = all.Count(b => b.Status == BlogStatus.Rejected),
                TotalArticles = articleCount,
                LastSync = lastUpdate
            });
        });
    }
}

public class SiteConfigUpdate
{
    public string? ReviewKey { get; set; }
    public string? SiteTitle { get; set; }
    public string? SiteDescription { get; set; }
    public string? HeadInjection { get; set; }
    public string? BodyStartInjection { get; set; }
    public string? BodyEndInjection { get; set; }
}
