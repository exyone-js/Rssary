using Tomlyn;
using Tomlyn.Model;
using BlogSwarm.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BlogSwarm.Services;

public class DataService
{
    private readonly string _dataDir;
    private readonly string _blogsDir;
    private readonly string _blogsPath;
    private readonly string _configPath;
    private readonly object _fileLock = new();
    private readonly Random _random = new();
    private readonly ICacheService _cache;
    private readonly ILogger<DataService> _logger;

    private const string CacheKeyBlogs = "blogs_all";
    private const string CacheKeyApprovedBlogs = "blogs_approved";
    private const string CacheKeyStats = "stats";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public DataService(IWebHostEnvironment env, ICacheService cache, ILogger<DataService> logger)
    {
        _dataDir = Path.Combine(env.ContentRootPath, "Data");
        _blogsDir = Path.Combine(_dataDir, "Blogs");
        _blogsPath = Path.Combine(_dataDir, "blogs.toml");
        _configPath = Path.Combine(_dataDir, "config.toml");
        _cache = cache;
        _logger = logger;
        EnsureDataDir();
    }

    private void EnsureDataDir()
    {
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_blogsDir);
        
        if (!File.Exists(_blogsPath))
        {
            File.WriteAllText(_blogsPath, "# BlogSwarm 博主列表\n");
        }
    }

    private string GetArticleFilePath(string blogId) => Path.Combine(_blogsDir, $"{blogId}.toml");

    public void InvalidateCache()
    {
        _cache.Remove(CacheKeyBlogs);
        _cache.Remove(CacheKeyApprovedBlogs);
        _cache.Remove(CacheKeyStats);
        _logger.LogDebug("Cache invalidated");
    }

    public BlogSwarmConfig LoadBlogs()
    {
        var cached = _cache.Get<BlogSwarmConfig>(CacheKeyBlogs);
        if (cached != null) return cached;

        lock (_fileLock)
        {
            if (!File.Exists(_blogsPath))
            {
                return new BlogSwarmConfig();
            }

            var text = File.ReadAllText(_blogsPath);
            var model = Toml.ToModel(text);
            var config = new BlogSwarmConfig();

            if (model.TryGetValue("blogs", out var blogsObj) && blogsObj is TomlTableArray tomlArray)
            {
                foreach (var item in tomlArray)
                {
                    if (item is TomlTable blogTable)
                    {
                        var id = GetString(blogTable, "id");
                        if (string.IsNullOrEmpty(id)) continue;

                        config.Blogs.Add(new BlogConfig
                        {
                            Id = id,
                            Name = GetString(blogTable, "name"),
                            Url = GetString(blogTable, "url"),
                            RssUrl = GetString(blogTable, "rss_url"),
                            Status = GetString(blogTable, "status", "Pending"),
                            SubmittedAt = GetString(blogTable, "submitted_at"),
                            ApprovedAt = GetStringOrNull(blogTable, "approved_at"),
                            Description = GetStringOrNull(blogTable, "description")
                        });
                    }
                }
            }

            _cache.Set(CacheKeyBlogs, config, CacheDuration);
            return config;
        }
    }

    private void SaveBlogs(BlogSwarmConfig config)
    {
        lock (_fileLock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# BlogSwarm 博主列表");
            sb.AppendLine();

            foreach (var blog in config.Blogs)
            {
                sb.AppendLine("[[blogs]]");
                sb.AppendLine($"id = \"{blog.Id}\"");
                sb.AppendLine($"name = \"{EscapeString(blog.Name)}\"");
                sb.AppendLine($"url = \"{EscapeString(blog.Url)}\"");
                sb.AppendLine($"rss_url = \"{EscapeString(blog.RssUrl)}\"");
                sb.AppendLine($"status = \"{blog.Status}\"");
                sb.AppendLine($"submitted_at = \"{blog.SubmittedAt}\"");
                if (!string.IsNullOrEmpty(blog.ApprovedAt))
                    sb.AppendLine($"approved_at = \"{blog.ApprovedAt}\"");
                if (!string.IsNullOrEmpty(blog.Description))
                    sb.AppendLine($"description = \"{EscapeString(blog.Description)}\"");
                sb.AppendLine();
            }

            File.WriteAllText(_blogsPath, sb.ToString());
            InvalidateCache();
        }
    }

    private ArticleFileConfig LoadArticlesByBlogId(string blogId)
    {
        var cacheKey = $"articles_{blogId}";
        var cached = _cache.Get<ArticleFileConfig>(cacheKey);
        if (cached != null) return cached;

        var filePath = GetArticleFilePath(blogId);
        var config = new ArticleFileConfig { BlogId = blogId };

        if (!File.Exists(filePath))
        {
            return config;
        }

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
                            Id = GetString(articleTable, "id"),
                            Title = GetString(articleTable, "title"),
                            Link = GetString(articleTable, "link"),
                            Description = GetStringOrNull(articleTable, "description"),
                            PublishedAt = GetString(articleTable, "published_at"),
                            FetchedAt = GetString(articleTable, "fetched_at"),
                            Author = GetStringOrNull(articleTable, "author")
                        });
                    }
                }
            }

            _cache.Set(cacheKey, config, CacheDuration);
            return config;
        }
    }

    private void SaveArticlesByBlogId(string blogId, List<ArticleConfig> articles)
    {
        var filePath = GetArticleFilePath(blogId);

        lock (_fileLock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# 博客文章 - {blogId}");
            sb.AppendLine();

            foreach (var article in articles)
            {
                sb.AppendLine("[[articles]]");
                sb.AppendLine($"id = \"{article.Id}\"");
                sb.AppendLine($"title = \"{EscapeString(article.Title)}\"");
                sb.AppendLine($"link = \"{EscapeString(article.Link)}\"");
                if (!string.IsNullOrEmpty(article.Description))
                    sb.AppendLine($"description = \"{EscapeString(article.Description)}\"");
                sb.AppendLine($"published_at = \"{article.PublishedAt}\"");
                sb.AppendLine($"fetched_at = \"{article.FetchedAt}\"");
                if (!string.IsNullOrEmpty(article.Author))
                    sb.AppendLine($"author = \"{EscapeString(article.Author)}\"");
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString());
            _cache.Remove($"articles_{blogId}");
        }
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static string GetString(TomlTable table, string key, string defaultValue = "")
    {
        return table.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;
    }

    private static string? GetStringOrNull(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value)) return null;
        var str = value?.ToString();
        return string.IsNullOrWhiteSpace(str) ? null : str;
    }

    public List<Blog> GetBlogs()
    {
        var config = LoadBlogs();
        return config.Blogs.Select(b => new Blog
        {
            Id = b.Id,
            Name = b.Name,
            Url = b.Url,
            RssUrl = b.RssUrl,
            Status = Enum.Parse<BlogStatus>(b.Status),
            SubmittedAt = DateTime.Parse(b.SubmittedAt),
            ApprovedAt = b.ApprovedAt != null ? DateTime.Parse(b.ApprovedAt) : null,
            Description = b.Description
        }).ToList();
    }

    public List<Article> GetArticles()
    {
        var blogs = GetApprovedBlogs();
        var allArticles = new List<Article>();

        foreach (var blog in blogs)
        {
            var articleConfig = LoadArticlesByBlogId(blog.Id);
            foreach (var a in articleConfig.Articles)
            {
                allArticles.Add(new Article
                {
                    Id = a.Id,
                    BlogId = blog.Id,
                    Title = a.Title,
                    Link = a.Link,
                    Description = a.Description,
                    PublishedAt = DateTime.Parse(a.PublishedAt),
                    FetchedAt = DateTime.Parse(a.FetchedAt),
                    Author = a.Author
                });
            }
        }

        return allArticles;
    }

    public List<Article> GetRandomArticles(int count = 100)
    {
        var blogs = GetApprovedBlogs();
        if (blogs.Count == 0) return [];

        var result = new List<Article>();
        var blogIds = blogs.Select(b => b.Id).ToList();

        lock (_random)
        {
            var shuffledBlogIds = blogIds.OrderBy(_ => _random.Next()).ToList();
            var articleIndex = new Dictionary<string, int>();
            var articleCounts = new Dictionary<string, int>();

            foreach (var blogId in shuffledBlogIds)
            {
                var articles = LoadArticlesByBlogId(blogId);
                articleCounts[blogId] = articles.Articles.Count;
                articleIndex[blogId] = 0;
            }

            while (result.Count < count)
            {
                var addedAny = false;

                foreach (var blogId in shuffledBlogIds)
                {
                    if (result.Count >= count) break;

                    var articleConfig = LoadArticlesByBlogId(blogId);
                    var idx = articleIndex[blogId];

                    if (idx < articleConfig.Articles.Count)
                    {
                        var a = articleConfig.Articles[idx];
                        result.Add(new Article
                        {
                            Id = a.Id,
                            BlogId = blogId,
                            Title = a.Title,
                            Link = a.Link,
                            Description = a.Description,
                            PublishedAt = DateTime.Parse(a.PublishedAt),
                            FetchedAt = DateTime.Parse(a.FetchedAt),
                            Author = a.Author
                        });
                        articleIndex[blogId]++;
                        addedAny = true;
                    }
                }

                if (!addedAny) break;
            }
        }

        return result;
    }

    public string GetReviewKey()
    {
        var cacheKey = "config_review_key";
        var cached = _cache.Get<string>(cacheKey);
        if (cached != null) return cached;

        lock (_fileLock)
        {
            if (!File.Exists(_configPath))
            {
                return "review";
            }

            try
            {
                var text = File.ReadAllText(_configPath);
                var model = Toml.ToModel(text);
                var key = GetString(model, "review_key", "review");
                _cache.Set(cacheKey, key, CacheDuration);
                return key;
            }
            catch
            {
                return "review";
            }
        }
    }

    public void AddBlog(Blog blog)
    {
        var config = LoadBlogs();
        config.Blogs.Add(new BlogConfig
        {
            Id = blog.Id,
            Name = blog.Name,
            Url = blog.Url,
            RssUrl = blog.RssUrl,
            Status = blog.Status.ToString(),
            SubmittedAt = blog.SubmittedAt.ToString("O"),
            ApprovedAt = blog.ApprovedAt?.ToString("O"),
            Description = blog.Description
        });
        SaveBlogs(config);
    }

    public void UpdateBlogStatus(string blogId, BlogStatus status)
    {
        var config = LoadBlogs();
        var blog = config.Blogs.FirstOrDefault(b => b.Id == blogId);
        if (blog != null)
        {
            blog.Status = status.ToString();
            if (status == BlogStatus.Approved)
            {
                blog.ApprovedAt = DateTime.UtcNow.ToString("O");
            }
            SaveBlogs(config);
        }
    }

    public void AddArticles(string blogId, List<Article> articles)
    {
        var existingConfig = LoadArticlesByBlogId(blogId);
        var existingLinks = existingConfig.Articles.Select(a => a.Link).ToHashSet();

        var newCount = 0;
        foreach (var article in articles)
        {
            if (!existingLinks.Contains(article.Link))
            {
                existingConfig.Articles.Add(new ArticleConfig
                {
                    Id = article.Id,
                    Title = article.Title,
                    Link = article.Link,
                    Description = article.Description,
                    PublishedAt = article.PublishedAt.ToString("O"),
                    FetchedAt = article.FetchedAt.ToString("O"),
                    Author = article.Author
                });
                newCount++;
            }
        }

        if (newCount > 0)
        {
            SaveArticlesByBlogId(blogId, existingConfig.Articles);
            _cache.Remove(CacheKeyStats);
            _logger.LogDebug("Added {Count} new articles for blog {BlogId}", newCount, blogId);
        }
    }

    public Blog? GetBlogById(string id)
    {
        return GetBlogs().FirstOrDefault(b => b.Id == id);
    }

    public List<Blog> GetApprovedBlogs()
    {
        var cached = _cache.Get<List<Blog>>(CacheKeyApprovedBlogs);
        if (cached != null) return cached;

        var blogs = GetBlogs().Where(b => b.Status == BlogStatus.Approved).ToList();
        _cache.Set(CacheKeyApprovedBlogs, blogs, CacheDuration);
        return blogs;
    }

    public List<Blog> GetPendingBlogs()
    {
        return GetBlogs().Where(b => b.Status == BlogStatus.Pending).ToList();
    }

    public List<Article> GetArticlesByBlogId(string blogId, int page = 1, int pageSize = 20)
    {
        var articleConfig = LoadArticlesByBlogId(blogId);
        return articleConfig.Articles
            .OrderByDescending(a => a.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new Article
            {
                Id = a.Id,
                BlogId = blogId,
                Title = a.Title,
                Link = a.Link,
                Description = a.Description,
                PublishedAt = DateTime.Parse(a.PublishedAt),
                FetchedAt = DateTime.Parse(a.FetchedAt),
                Author = a.Author
            }).ToList();
    }

    public int GetArticleCountByBlogId(string blogId)
    {
        var articleConfig = LoadArticlesByBlogId(blogId);
        return articleConfig.Articles.Count;
    }

    public List<Article> GetLatestArticles(int count = 50)
    {
        var blogs = GetApprovedBlogs();
        var allArticles = new List<Article>();

        foreach (var blog in blogs)
        {
            var articleConfig = LoadArticlesByBlogId(blog.Id);
            foreach (var a in articleConfig.Articles)
            {
                allArticles.Add(new Article
                {
                    Id = a.Id,
                    BlogId = blog.Id,
                    Title = a.Title,
                    Link = a.Link,
                    Description = a.Description,
                    PublishedAt = DateTime.Parse(a.PublishedAt),
                    FetchedAt = DateTime.Parse(a.FetchedAt),
                    Author = a.Author
                });
            }
        }

        return allArticles
            .OrderByDescending(a => a.PublishedAt)
            .Take(count)
            .ToList();
    }

    public (List<Article> Articles, int TotalCount) GetArticlesPaged(int page = 1, int pageSize = 20)
    {
        var blogs = GetApprovedBlogs();
        var allArticles = new List<Article>();

        foreach (var blog in blogs)
        {
            var articleConfig = LoadArticlesByBlogId(blog.Id);
            foreach (var a in articleConfig.Articles)
            {
                allArticles.Add(new Article
                {
                    Id = a.Id,
                    BlogId = blog.Id,
                    Title = a.Title,
                    Link = a.Link,
                    Description = a.Description,
                    PublishedAt = DateTime.Parse(a.PublishedAt),
                    FetchedAt = DateTime.Parse(a.FetchedAt),
                    Author = a.Author
                });
            }
        }

        var sorted = allArticles.OrderByDescending(a => a.PublishedAt).ToList();
        var paged = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (paged, sorted.Count);
    }

    public List<Article> SearchArticles(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var blogs = GetApprovedBlogs();
        var blogMap = blogs.ToDictionary(b => b.Id);
        var results = new List<Article>();
        var queryLower = query.ToLowerInvariant();

        foreach (var blog in blogs)
        {
            var articleConfig = LoadArticlesByBlogId(blog.Id);
            foreach (var a in articleConfig.Articles)
            {
                var titleMatch = a.Title.ToLowerInvariant().Contains(queryLower);
                var descMatch = a.Description?.ToLowerInvariant().Contains(queryLower) ?? false;
                var blogNameMatch = blogMap.TryGetValue(blog.Id, out var b) && b.Name.ToLowerInvariant().Contains(queryLower);

                if (titleMatch || descMatch || blogNameMatch)
                {
                    results.Add(new Article
                    {
                        Id = a.Id,
                        BlogId = blog.Id,
                        Title = a.Title,
                        Link = a.Link,
                        Description = a.Description,
                        PublishedAt = DateTime.Parse(a.PublishedAt),
                        FetchedAt = DateTime.Parse(a.FetchedAt),
                        Author = a.Author
                    });
                }
            }
        }

        return results.OrderByDescending(a => a.PublishedAt).ToList();
    }

    public List<(Blog Blog, int ArticleCount)> GetTopBlogs(int count = 10)
    {
        var blogs = GetApprovedBlogs();
        var result = new List<(Blog Blog, int ArticleCount)>();

        foreach (var blog in blogs)
        {
            var articleCount = GetArticleCountByBlogId(blog.Id);
            result.Add((blog, articleCount));
        }

        return result
            .OrderByDescending(x => x.ArticleCount)
            .Take(count)
            .ToList();
    }

    public List<Blog> GetRandomBlogs(int count = 5)
    {
        var blogs = GetApprovedBlogs();
        if (blogs.Count == 0) return [];

        lock (_random)
        {
            return blogs
                .OrderBy(_ => _random.Next())
                .Take(count)
                .ToList();
        }
    }

    public List<Article> GetRecentArticles(int count = 10)
    {
        return GetLatestArticles(count);
    }

    public (int BlogCount, int ArticleCount, DateTime? LastUpdate) GetStats()
    {
        var cached = _cache.Get<(int, int, DateTime?)>(CacheKeyStats);
        if (cached != default) return cached;

        var blogs = GetApprovedBlogs();
        var articles = GetArticles();
        var lastUpdate = articles.Any() ? articles.Max(a => a.FetchedAt) : (DateTime?)null;

        var stats = (blogs.Count, articles.Count, lastUpdate);
        _cache.Set(CacheKeyStats, stats, CacheDuration);
        return stats;
    }

    public (string Text, string Author)? GetRandomQuote()
    {
        var quotesPath = Path.Combine(_dataDir, "quotes.toml");
        
        if (!File.Exists(quotesPath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(quotesPath);
            var model = Toml.ToModel(text);
            var quotes = new List<(string Text, string Author)>();

            if (model.TryGetValue("quotes", out var quotesObj) && quotesObj is TomlTableArray tomlArray)
            {
                foreach (var item in tomlArray)
                {
                    if (item is TomlTable quoteTable)
                    {
                        var quoteText = GetString(quoteTable, "text");
                        var author = GetString(quoteTable, "author");
                        if (!string.IsNullOrEmpty(quoteText))
                        {
                            quotes.Add((quoteText, author));
                        }
                    }
                }
            }

            if (quotes.Count == 0) return null;

            lock (_random)
            {
                return quotes[_random.Next(quotes.Count)];
            }
        }
        catch
        {
            return null;
        }
    }
}
