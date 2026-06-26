using Tomlyn;
using Tomlyn.Model;
using Rssary.DomainModels;
using Rssary.Caching;

namespace Rssary.DataStore;

/// <summary>
/// {blogId}.toml 读写 —— 文章数据存储
/// 使用按博客缓存 + 全局索引缓存实现高性能
/// </summary>
public class ArticleStore
{
    private readonly string _articlesDir;
    private readonly object _fileLock = new();
    private readonly ICache _cache;
    private readonly ILogger<ArticleStore> _logger;

    private const string IndexCacheKey = "article_global_index";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IndexCacheDuration = TimeSpan.FromMinutes(10);

    public ArticleStore(string articlesDir, ICache cache, ILogger<ArticleStore> logger)
    {
        _articlesDir = articlesDir;
        _cache = cache;
        _logger = logger;
    }

    private string GetFilePath(string blogId) => Path.Combine(_articlesDir, $"{blogId}.toml");

    // ===== 单博客读取 =====

    private ArticleFileConfig ReadConfig(string blogId)
    {
        var cacheKey = $"articles_{blogId}";
        var cached = _cache.Get<ArticleFileConfig>(cacheKey);
        if (cached != null) return cached;

        var filePath = GetFilePath(blogId);
        var config = new ArticleFileConfig { BlogId = blogId };

        if (!File.Exists(filePath)) return config;

        lock (_fileLock)
        {
            var text = File.ReadAllText(filePath);
            var model = Toml.ToModel(text);

            if (model.TryGetValue("articles", out var articlesObj) && articlesObj is TomlTableArray tomlArray)
            {
                foreach (var item in tomlArray)
                {
                    if (item is TomlTable articleTable)
                    {
                        config.Articles.Add(new ArticleConfig
                        {
                            Id = TomlHelper.GetString(articleTable, "id"),
                            Title = TomlHelper.GetString(articleTable, "title"),
                            Link = TomlHelper.GetString(articleTable, "link"),
                            Description = TomlHelper.GetStringOrNull(articleTable, "description"),
                            PublishedAt = TomlHelper.GetString(articleTable, "published_at"),
                            FetchedAt = TomlHelper.GetString(articleTable, "fetched_at"),
                            Author = TomlHelper.GetStringOrNull(articleTable, "author")
                        });
                    }
                }
            }

            _cache.Set(cacheKey, config, CacheDuration);
            return config;
        }
    }

    private void WriteConfig(string blogId, List<ArticleConfig> articles)
    {
        var filePath = GetFilePath(blogId);

        lock (_fileLock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# 博客文章 - {blogId}");
            sb.AppendLine();

            foreach (var article in articles)
            {
                sb.AppendLine("[[articles]]");
                sb.AppendLine($"id = \"{article.Id}\"");
                sb.AppendLine($"title = \"{TomlHelper.Escape(article.Title)}\"");
                sb.AppendLine($"link = \"{TomlHelper.Escape(article.Link)}\"");
                if (!string.IsNullOrEmpty(article.Description))
                    sb.AppendLine($"description = \"{TomlHelper.Escape(article.Description)}\"");
                sb.AppendLine($"published_at = \"{article.PublishedAt}\"");
                sb.AppendLine($"fetched_at = \"{article.FetchedAt}\"");
                if (!string.IsNullOrEmpty(article.Author))
                    sb.AppendLine($"author = \"{TomlHelper.Escape(article.Author)}\"");
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString());
            _cache.Remove($"articles_{blogId}");
            _cache.Remove(IndexCacheKey); // 全局索引失效
        }
    }

    private static Article MapToDomain(ArticleConfig a, string blogId) => new()
    {
        Id = a.Id,
        BlogId = blogId,
        Title = a.Title,
        Link = a.Link,
        Description = a.Description,
        PublishedAt = DateTime.Parse(a.PublishedAt),
        FetchedAt = DateTime.Parse(a.FetchedAt),
        Author = a.Author
    };

    // ===== 单博客查询 =====

    public List<Article> GetByBlogId(string blogId, int page = 1, int pageSize = 20)
    {
        var config = ReadConfig(blogId);
        return config.Articles
            .OrderByDescending(a => a.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => MapToDomain(a, blogId))
            .ToList();
    }

    public int GetCountByBlogId(string blogId) => ReadConfig(blogId).Articles.Count;

    // ===== 跨博客高性能索引 =====

    /// <summary>
    /// 获取全局文章索引（跨所有博主，已排序），使用缓存避免重复文件IO
    /// </summary>
    public List<Article> GetGlobalIndex(IEnumerable<string> approvedBlogIds)
    {
        var cached = _cache.Get<List<Article>>(IndexCacheKey);
        if (cached != null) return cached;

        var blogIds = approvedBlogIds.ToList();
        var all = new List<Article>(blogIds.Count * 100);

        foreach (var id in blogIds)
        {
            var config = ReadConfig(id);
            foreach (var a in config.Articles)
                all.Add(MapToDomain(a, id));
        }

        // 排序后缓存
        all = all.OrderByDescending(a => a.PublishedAt).ToList();
        _cache.Set(IndexCacheKey, all, IndexCacheDuration);
        return all;
    }

    // ===== 新增文章 =====

    public void AddNew(string blogId, List<Article> newArticles)
    {
        var existing = ReadConfig(blogId);
        var existingLinks = existing.Articles.Select(a => a.Link).ToHashSet();

        var added = 0;
        foreach (var article in newArticles)
        {
            if (!existingLinks.Contains(article.Link))
            {
                existing.Articles.Add(new ArticleConfig
                {
                    Id = article.Id,
                    Title = article.Title,
                    Link = article.Link,
                    Description = article.Description,
                    PublishedAt = article.PublishedAt.ToString("O"),
                    FetchedAt = article.FetchedAt.ToString("O"),
                    Author = article.Author
                });
                added++;
            }
        }

        if (added > 0)
        {
            WriteConfig(blogId, existing.Articles);
            _logger.LogDebug("Added {Count} new articles for blog {BlogId}", added, blogId);
        }
    }
}
