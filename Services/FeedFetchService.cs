using BlogSwarm.Models;

namespace BlogSwarm.Services;

public class FeedFetchService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeedFetchService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(8);
    private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(10);
    private readonly int _maxDegreeOfParallelism = 3;
    private readonly TimeSpan _staggerDelay = TimeSpan.FromSeconds(2);

    public FeedFetchService(IServiceProvider serviceProvider, ILogger<FeedFetchService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FeedFetchService starting, initial delay: {Delay}s", _initialDelay.TotalSeconds);
        await Task.Delay(_initialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAllFeedsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in feed fetch cycle");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("FeedFetchService stopped");
    }

    private async Task FetchAllFeedsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dataService = scope.ServiceProvider.GetRequiredService<DataService>();
        var rssService = scope.ServiceProvider.GetRequiredService<RssService>();

        var blogs = dataService.GetApprovedBlogs();
        
        if (blogs.Count == 0)
        {
            _logger.LogInformation("No approved blogs to fetch");
            return;
        }

        _logger.LogInformation("Starting feed fetch for {Count} blogs with max parallelism {MaxParallel}", 
            blogs.Count, _maxDegreeOfParallelism);

        var successCount = 0;
        var errorCount = 0;
        var totalArticles = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(blogs, parallelOptions, async (blog, ct) =>
        {
            try
            {
                await Task.Delay(_staggerDelay, ct);
                
                var articles = await rssService.FetchArticlesAsync(blog, ct);
                
                if (articles.Count > 0)
                {
                    dataService.AddArticles(blog.Id, articles);
                    Interlocked.Add(ref totalArticles, articles.Count);
                    Interlocked.Increment(ref successCount);
                    _logger.LogInformation("Fetched {Count} articles from {Name}", articles.Count, blog.Name);
                }
                else
                {
                    _logger.LogDebug("No articles fetched from {Name}", blog.Name);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errorCount);
                _logger.LogWarning(ex, "Failed to fetch feed for {Name}", blog.Name);
            }
        });

        _logger.LogInformation(
            "Feed fetch completed: {Success} succeeded, {Error} failed, {Total} total articles",
            successCount, errorCount, totalArticles);
    }
}
