using Rssary.DomainModels;
using Rssary.DataStore;

namespace Rssary.RssReading;

/// <summary>
/// 定时 RSS 后台同步任务
/// </summary>
public class FeedSyncJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FeedSyncJob> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(8);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);

    public FeedSyncJob(IServiceProvider services, ILogger<FeedSyncJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FeedSyncJob starting, initial delay: {Delay}s", InitialDelay.TotalSeconds);
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllFeedsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in feed sync cycle");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("FeedSyncJob stopped");
    }

    private async Task SyncAllFeedsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var blogs = scope.ServiceProvider.GetRequiredService<BlogStore>();
        var articles = scope.ServiceProvider.GetRequiredService<ArticleStore>();
        var reader = scope.ServiceProvider.GetRequiredService<RssFeedReader>();

        var approvedBlogs = blogs.GetApproved();
        if (approvedBlogs.Count == 0) return;

        _logger.LogInformation("Syncing {Count} feeds", approvedBlogs.Count);

        var success = 0;
        var total = 0;

        await Parallel.ForEachAsync(approvedBlogs, new ParallelOptions
        {
            MaxDegreeOfParallelism = 3,
            CancellationToken = ct
        }, async (blog, innerCt) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), innerCt);
                var fetched = await reader.FetchArticlesAsync(blog, innerCt);
                if (fetched.Count > 0)
                {
                    articles.AddNew(blog.Id, fetched);
                    Interlocked.Add(ref total, fetched.Count);
                    Interlocked.Increment(ref success);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync {Name}", blog.Name);
            }
        });

        _logger.LogInformation("Sync completed: {Success} OK, {Total} articles", success, total);
    }
}
