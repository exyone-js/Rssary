using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using BlogSwarm.Models;
using BlogSwarm.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton<DataService>();
builder.Services.AddSingleton<RssService>();
builder.Services.AddHostedService<FeedFetchService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/feed", (DataService dataService, HttpContext context) =>
{
    var articles = dataService.GetLatestArticles(50);
    var blogs = dataService.GetApprovedBlogs();
    var blogMap = blogs.ToDictionary(b => b.Id);

    var feed = new SyndicationFeed(
        "BlogSwarm - 文章聚合",
        "BlogSwarm 是一个去中心化的 RSS/Feed 整合平台",
        new Uri($"{context.Request.Scheme}://{context.Request.Host}"),
        "blogswarm-main",
        DateTime.UtcNow
    );

    var items = new List<SyndicationItem>();
    foreach (var article in articles)
    {
        var item = new SyndicationItem(
            article.Title,
            article.Description ?? "",
            new Uri(article.Link),
            article.Id,
            article.PublishedAt
        );

        if (blogMap.TryGetValue(article.BlogId, out var blog))
        {
            item.Authors.Add(new SyndicationPerson { Name = blog.Name });
        }

        if (!string.IsNullOrEmpty(article.Author))
        {
            item.Authors.Add(new SyndicationPerson { Name = article.Author });
        }

        items.Add(item);
    }

    feed.Items = items;

    var settings = new XmlWriterSettings
    {
        Encoding = Encoding.UTF8,
        NewLineHandling = NewLineHandling.Entitize,
        NewLineOnAttributes = false,
        Indent = true
    };

    using var memoryStream = new MemoryStream();
    using var xmlWriter = XmlWriter.Create(memoryStream, settings);

    var rssFormatter = new Rss20FeedFormatter(feed);
    rssFormatter.WriteTo(xmlWriter);
    xmlWriter.Flush();

    var result = Encoding.UTF8.GetString(memoryStream.ToArray());
    return Results.Text(result, "application/rss+xml", Encoding.UTF8);
});

app.MapGet("/api/blogs", (DataService dataService) =>
{
    var blogs = dataService.GetApprovedBlogs();
    return Results.Ok(blogs.Select(b => new
    {
        b.Id,
        b.Name,
        b.Url,
        b.RssUrl,
        b.Description
    }));
});

app.MapGet("/api/blog/{id}", (string id, DataService dataService) =>
{
    var blog = dataService.GetBlogById(id);
    if (blog == null || blog.Status != BlogStatus.Approved)
    {
        return Results.NotFound();
    }

    var articleCount = dataService.GetArticleCountByBlogId(id);
    return Results.Ok(new
    {
        blog.Id,
        blog.Name,
        blog.Url,
        blog.RssUrl,
        blog.Description,
        ArticleCount = articleCount
    });
});

app.MapGet("/api/blog/{id}/articles", (string id, DataService dataService, int page = 1, int pageSize = 20) =>
{
    var blog = dataService.GetBlogById(id);
    if (blog == null || blog.Status != BlogStatus.Approved)
    {
        return Results.NotFound();
    }

    var articles = dataService.GetArticlesByBlogId(id, page, pageSize);
    var totalCount = dataService.GetArticleCountByBlogId(id);

    return Results.Ok(new
    {
        Blog = new { blog.Id, blog.Name, blog.Url },
        Articles = articles.Select(a => new
        {
            a.Id,
            a.Title,
            a.Link,
            a.Description,
            a.PublishedAt,
            a.Author
        }),
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
    });
});

app.MapGet("/api/articles", (DataService dataService, int page = 1, int pageSize = 20) =>
{
    var (articles, totalCount) = dataService.GetArticlesPaged(page, pageSize);
    var blogs = dataService.GetApprovedBlogs();
    var blogMap = blogs.ToDictionary(b => b.Id);

    return Results.Ok(new
    {
        Articles = articles.Select(a => new
        {
            a.Id,
            a.Title,
            a.Link,
            a.Description,
            a.PublishedAt,
            a.Author,
            Blog = blogMap.TryGetValue(a.BlogId, out var blog) ? new { blog.Id, blog.Name } : null
        }),
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
    });
});

app.MapGet("/api/search", (string q, DataService dataService) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { Error = "Query parameter 'q' is required" });
    }

    var articles = dataService.SearchArticles(q);
    var blogs = dataService.GetApprovedBlogs();
    var blogMap = blogs.ToDictionary(b => b.Id);

    return Results.Ok(new
    {
        Query = q,
        Count = articles.Count,
        Articles = articles.Select(a => new
        {
            a.Id,
            a.Title,
            a.Link,
            a.Description,
            a.PublishedAt,
            a.Author,
            Blog = blogMap.TryGetValue(a.BlogId, out var blog) ? new { blog.Id, blog.Name } : null
        })
    });
});

app.MapGet("/api/stats", (DataService dataService) =>
{
    var blogs = dataService.GetApprovedBlogs();
    var articles = dataService.GetArticles();

    return Results.Ok(new
    {
        BlogCount = blogs.Count,
        ArticleCount = articles.Count,
        LastUpdated = articles.Any() ? articles.Max(a => a.FetchedAt) : (DateTime?)null
    });
});

app.Run();
