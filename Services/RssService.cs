using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using BlogSwarm.Models;

namespace BlogSwarm.Services;

public class RssService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RssService> _logger;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrentRequests = 3;
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _rateLimitDelay = TimeSpan.FromMilliseconds(500);

    public RssService(ILogger<RssService> logger)
    {
        _logger = logger;
        _semaphore = new SemaphoreSlim(_maxConcurrentRequests, _maxConcurrentRequests);
        
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = _requestTimeout
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlogSwarm/1.0 (RSS Aggregator)");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml, */*");
    }

    public async Task<List<Article>> FetchArticlesAsync(Blog blog, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(_rateLimitDelay, cancellationToken);
            return await FetchArticlesInternalAsync(blog, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<Article>> FetchArticlesInternalAsync(Blog blog, CancellationToken cancellationToken)
    {
        var articles = new List<Article>();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, blog.RssUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
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
                    Description = TruncateDescription(item.Summary?.Text, 500),
                    PublishedAt = item.PublishDate.UtcDateTime,
                    Author = item.Authors.FirstOrDefault()?.Name,
                    FetchedAt = DateTime.UtcNow
                });
            }

            _logger.LogDebug("Fetched {Count} articles from {Name}", articles.Count, blog.Name);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error fetching RSS for {Name}: {Message}", blog.Name, ex.Message);
        }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex, "XML parsing error for {Name}: {Message}", blog.Name, ex.Message);
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

    public async Task<bool> ValidateRssUrlAsync(string rssUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rssUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode) return false;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
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

    private static string? TruncateDescription(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        
        text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = text.Trim();
        
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "...";
    }
}
