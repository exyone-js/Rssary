using BlogSwarm.Models;

namespace BlogSwarm.Services;

public class FeedFetchService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeedFetchService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(8);

    public FeedFetchService(IServiceProvider serviceProvider, ILogger<FeedFetchService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAllFeedsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching feeds");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task FetchAllFeedsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dataService = scope.ServiceProvider.GetRequiredService<DataService>();
        var rssService = scope.ServiceProvider.GetRequiredService<RssService>();

        var blogs = dataService.GetApprovedBlogs();
        _logger.LogInformation("Fetching feeds for {Count} blogs", blogs.Count);

        foreach (var blog in blogs)
        {
            try
            {
                var articles = await rssService.FetchArticlesAsync(blog);
                dataService.AddArticles(blog.Id, articles);
                _logger.LogInformation("Fetched {Count} articles from {Name}", articles.Count, blog.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching feed for {Name}", blog.Name);
            }
        }
    }
}
