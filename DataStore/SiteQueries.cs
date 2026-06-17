using Rssary.DomainModels;
using Rssary.Caching;

namespace Rssary.DataStore;

/// <summary>
/// 跨 BlogStore + ArticleStore 的聚合查询
/// 所有跨博客查询走 ArticleStore 全局索引缓存
/// </summary>
public class SiteQueries
{
    private readonly BlogStore _blogs;
    private readonly ArticleStore _articles;
    private readonly ICache _cache;

    private const string CacheKeyStats = "stats";

    public SiteQueries(BlogStore blogStore, ArticleStore articleStore, ICache cache)
    {
        _blogs = blogStore;
        _articles = articleStore;
        _cache = cache;
    }

    private List<string> ApprovedBlogIds => _blogs.GetApproved().Select(b => b.Id).ToList();

    public List<Article> GetAllArticles()
    {
        return _articles.GetGlobalIndex(ApprovedBlogIds);
    }

    public List<Article> GetLatest(int count = 50)
    {
        return _articles.GetGlobalIndex(ApprovedBlogIds).Take(count).ToList();
    }

    public (List<Article> Articles, int TotalCount) GetPaged(int page = 1, int pageSize = 20)
    {
        var all = _articles.GetGlobalIndex(ApprovedBlogIds);
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (paged, all.Count);
    }

    public (List<Article> Articles, int Count) Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return ([], 0);

        var blogs = _blogs.GetApproved();
        var q = query.ToLowerInvariant();
        var results = new List<Article>();

        foreach (var blog in blogs)
        {
            var arts = _articles.GetByBlogId(blog.Id, 1, int.MaxValue);
            foreach (var a in arts)
            {
                if (a.Title.ToLowerInvariant().Contains(q) ||
                    (a.Description?.ToLowerInvariant().Contains(q) ?? false) ||
                    blog.Name.ToLowerInvariant().Contains(q))
                {
                    results.Add(a);
                }
            }
        }

        return (results.OrderByDescending(a => a.PublishedAt).ToList(), results.Count);
    }

    public List<(Blog Blog, int Count)> GetTopBlogs(int count = 10)
    {
        return _blogs.GetApproved()
            .Select(b => (b, _articles.GetCountByBlogId(b.Id)))
            .OrderByDescending(x => x.Item2)
            .Take(count)
            .ToList();
    }

    public List<Article> GetRandom(int count = 100)
    {
        var all = _articles.GetGlobalIndex(ApprovedBlogIds);
        if (all.Count == 0) return [];
        return all.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }

    public (int BlogCount, int ArticleCount, DateTime? LastUpdate) GetStats()
    {
        var cached = _cache.Get<(int, int, DateTime?)>(CacheKeyStats);
        if (cached != default) return cached;

        var blogs = _blogs.GetApproved();
        var allArticles = _articles.GetGlobalIndex(ApprovedBlogIds);
        var lastUpdate = allArticles.Count > 0 ? allArticles.Max(a => a.FetchedAt) : (DateTime?)null;
        var stats = (blogs.Count, allArticles.Count, lastUpdate);

        _cache.Set(CacheKeyStats, stats, TimeSpan.FromMinutes(30));
        return stats;
    }
}
