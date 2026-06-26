using Rssary.DomainModels;
using Rssary.DataStore;

namespace Rssary.ApiEndpoints;

/// <summary>
/// 博客相关 API：列表、详情、检查ID、提交
/// </summary>
public static class BlogEndpoints
{
    public static void MapBlogEndpoints(this WebApplication app)
    {
        // 所有已审核博客
        app.MapGet("/api/blogs", (BlogStore blogs) =>
        {
            return Results.Ok(blogs.GetApproved().Select(b => new
            {
                b.Id, b.Name, b.Url, b.RssUrl, b.Description
            }));
        }).CacheOutput("ApiPolicy");

        // 博客详情
        app.MapGet("/api/blog/{id}", (string id, BlogStore blogs, ArticleStore articles) =>
        {
            var blog = blogs.GetById(id);
            if (blog == null || blog.Status != BlogStatus.Approved)
                return Results.NotFound();

            var articleCount = articles.GetCountByBlogId(id);
            return Results.Ok(new
            {
                blog.Id, blog.Name, blog.Url, blog.RssUrl,
                blog.Description, ArticleCount = articleCount
            });
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(15)));

        // 博客文章列表
        app.MapGet("/api/blog/{id}/articles", (string id, BlogStore blogs, ArticleStore articles,
            int page = 1, int pageSize = 20) =>
        {
            var blog = blogs.GetById(id);
            if (blog == null || blog.Status != BlogStatus.Approved)
                return Results.NotFound();

            var list = articles.GetByBlogId(id, page, pageSize);
            var total = articles.GetCountByBlogId(id);

            return Results.Ok(new
            {
                Blog = new { blog.Id, blog.Name, blog.Url },
                Articles = list.Select(a => new
                {
                    a.Id, a.Title, a.Link, a.Description, a.PublishedAt, a.Author
                }),
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(15)));

        // 检查ID可用性
        app.MapGet("/api/check-id/{id}", (string id, BlogStore blogs) =>
        {
            if (!Blog.IsValidId(id))
                return Results.Ok(new { Available = false, Reason = "ID格式无效，只允许小写字母和数字，长度3-30" });

            var exists = blogs.ExistsId(id);
            return Results.Ok(new { Available = !exists, Reason = exists ? "该ID已被使用" : "ID可用" });
        });

        // 提交博客
        app.MapPost("/api/submit", async (BlogSubmission input, BlogStore blogs,
            ArticleStore articles, RssReading.RssFeedReader reader) =>
        {
            if (!Blog.IsValidId(input.Id))
                return Results.BadRequest(new { Error = "ID格式无效，只允许小写字母和数字，长度3-30" });

            if (blogs.ExistsId(input.Id))
                return Results.BadRequest(new { Error = "该ID已被使用，请选择其他ID" });

            var isValidRss = await reader.ValidateRssUrlAsync(input.RssUrl);
            if (!isValidRss)
                return Results.BadRequest(new { Error = "RSS 链接无效或无法访问" });

            var allBlogs = blogs.GetAll();
            if (allBlogs.Any(b => b.RssUrl == input.RssUrl || b.Url == input.Url))
                return Results.BadRequest(new { Error = "该博客已存在" });

            var blog = new Blog
            {
                Id = input.Id.ToLowerInvariant(),
                Name = input.Name,
                Url = input.Url,
                RssUrl = input.RssUrl,
                Description = input.Description,
                Status = BlogStatus.Pending,
                SubmittedAt = DateTime.UtcNow
            };

            blogs.Add(blog);
            return Results.Ok(new { Message = "博客提交成功，等待审核", BlogId = blog.Id });
        });
    }
}

/// <summary>
/// 博客提交请求体
/// </summary>
public class BlogSubmission
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
}
