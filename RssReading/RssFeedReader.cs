using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using Rssary.DomainModels;

namespace Rssary.RssReading;

/// <summary>
/// RSS 源读取 + 验证
/// </summary>
public class RssFeedReader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RssFeedReader> _logger;
    private readonly SemaphoreSlim _semaphore = new(3, 3);
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(500);

    public RssFeedReader(ILogger<RssFeedReader> logger)
    {
        _logger = logger;

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Rssary/1.0 (RSS Aggregator)");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml, */*");
    }

    public async Task<List<Article>> FetchArticlesAsync(Blog blog, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            await Task.Delay(RateLimitDelay, ct);
            return await FetchInternalAsync(blog, ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<Article>> FetchInternalAsync(Blog blog, CancellationToken ct)
    {
        var articles = new List<Article>();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, blog.RssUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            var feed = SyndicationFeed.Load(reader);

            foreach (var item in feed.Items.Take(50))
            {
                articles.Add(new Article
                {
                    BlogId = blog.Id,
                    Title = item.Title?.Text ?? "Untitled",
                    Link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                    Description = Truncate(item.Summary?.Text, 500),
                    PublishedAt = item.PublishDate.UtcDateTime,
                    Author = item.Authors.FirstOrDefault()?.Name,
                    FetchedAt = DateTime.UtcNow
                });
            }

            _logger.LogDebug("Fetched {Count} articles from {Name}", articles.Count, blog.Name);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error fetching RSS for {Name}", blog.Name);
        }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex, "XML parsing error for {Name}", blog.Name);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout fetching RSS for {Name}", blog.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching RSS for {Name}", blog.Name);
        }

        return articles;
    }

    public async Task<bool> ValidateRssUrlAsync(string rssUrl, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rssUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            SyndicationFeed.Load(reader);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text).Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
