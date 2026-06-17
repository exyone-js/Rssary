using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Rssary.DataStore;

namespace Rssary.ApiEndpoints;

/// <summary>
/// /feed —— RSS 输出
/// </summary>
public static class FeedEndpoint
{
    public static void MapFeedEndpoint(this WebApplication app)
    {
        app.MapGet("/feed", (SiteQueries site, HttpContext context) =>
        {
            var articles = site.GetLatest(50);
            var blogs = site.GetTopBlogs(50);
            var blogMap = blogs.ToDictionary(b => b.Blog.Id, b => b.Blog);

            var feed = new SyndicationFeed(
                "Rssary - 文章聚合",
                "Rssary 是一个去中心化的 RSS/Feed 整合平台",
                new Uri($"{context.Request.Scheme}://{context.Request.Host}"),
                "rssary-main",
                DateTime.UtcNow
            );

            var items = articles.Select(a =>
            {
                var item = new SyndicationItem(
                    a.Title, a.Description ?? "", new Uri(a.Link), a.Id, a.PublishedAt);
                if (blogMap.TryGetValue(a.BlogId, out var b))
                    item.Authors.Add(new SyndicationPerson { Name = b.Name });
                if (!string.IsNullOrEmpty(a.Author))
                    item.Authors.Add(new SyndicationPerson { Name = a.Author });
                return item;
            }).ToList();

            feed.Items = items;

            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.Entitize,
                Indent = true
            };

            using var ms = new MemoryStream();
            using var xw = XmlWriter.Create(ms, settings);
            new Rss20FeedFormatter(feed).WriteTo(xw);
            xw.Flush();

            return Results.Text(Encoding.UTF8.GetString(ms.ToArray()), "application/rss+xml", Encoding.UTF8);
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(30)));
    }
}
