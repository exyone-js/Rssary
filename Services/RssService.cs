using System.ServiceModel.Syndication;
using System.Xml;
using BlogSwarm.Models;

namespace BlogSwarm.Services;

public class RssService
{
    private readonly HttpClient _httpClient;

    public RssService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlogSwarm/1.0");
    }

    public async Task<List<Article>> FetchArticlesAsync(Blog blog)
    {
        var articles = new List<Article>();

        try
        {
            var response = await _httpClient.GetStringAsync(blog.RssUrl);
            using var reader = XmlReader.Create(new StringReader(response));
            var feed = SyndicationFeed.Load(reader);

            foreach (var item in feed.Items)
            {
                articles.Add(new Article
                {
                    BlogId = blog.Id,
                    Title = item.Title?.Text ?? "Untitled",
                    Link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                    Description = item.Summary?.Text,
                    PublishedAt = item.PublishDate.UtcDateTime,
                    Author = item.Authors.FirstOrDefault()?.Name,
                    FetchedAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching RSS for {blog.Name}: {ex.Message}");
        }

        return articles;
    }

    public async Task<bool> ValidateRssUrlAsync(string rssUrl)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(rssUrl);
            using var reader = XmlReader.Create(new StringReader(response));
            SyndicationFeed.Load(reader);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
