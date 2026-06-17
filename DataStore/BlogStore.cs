using Tomlyn;
using Tomlyn.Model;
using Rssary.DomainModels;
using Rssary.Caching;

namespace Rssary.DataStore;

/// <summary>
/// blogs.toml 读写 —— 博主数据存储
/// </summary>
public class BlogStore
{
    private readonly string _blogsPath;
    private readonly object _fileLock = new();
    private readonly ICache _cache;
    private readonly ILogger<BlogStore> _logger;

    private const string CacheKeyAll = "blogs_all";
    private const string CacheKeyApproved = "blogs_approved";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public BlogStore(string blogsPath, ICache cache, ILogger<BlogStore> logger)
    {
        _blogsPath = blogsPath;
        _cache = cache;
        _logger = logger;
    }

    // ===== 内部 TOML 读写 =====

    private RssaryConfig ReadConfig()
    {
        var cached = _cache.Get<RssaryConfig>(CacheKeyAll);
        if (cached != null) return cached;

        lock (_fileLock)
        {
            if (!File.Exists(_blogsPath)) return new RssaryConfig();

            var text = File.ReadAllText(_blogsPath);
            var model = Toml.ToModel(text);
            var config = new RssaryConfig();

            if (model.TryGetValue("blogs", out var blogsObj) && blogsObj is TomlTableArray tomlArray)
            {
                foreach (var item in tomlArray)
                {
                    if (item is TomlTable blogTable)
                    {
                        var id = TomlHelper.GetString(blogTable, "id");
                        if (string.IsNullOrEmpty(id)) continue;

                        config.Blogs.Add(new BlogConfig
                        {
                            Id = id,
                            Name = TomlHelper.GetString(blogTable, "name"),
                            Url = TomlHelper.GetString(blogTable, "url"),
                            RssUrl = TomlHelper.GetString(blogTable, "rss_url"),
                            Status = TomlHelper.GetString(blogTable, "status", "Pending"),
                            SubmittedAt = TomlHelper.GetString(blogTable, "submitted_at"),
                            ApprovedAt = TomlHelper.GetStringOrNull(blogTable, "approved_at"),
                            Description = TomlHelper.GetStringOrNull(blogTable, "description")
                        });
                    }
                }
            }

            _cache.Set(CacheKeyAll, config, CacheDuration);
            return config;
        }
    }

    private void WriteConfig(RssaryConfig config)
    {
        lock (_fileLock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Rssary 博主列表");
            sb.AppendLine();

            foreach (var blog in config.Blogs)
            {
                sb.AppendLine("[[blogs]]");
                sb.AppendLine($"id = \"{blog.Id}\"");
                sb.AppendLine($"name = \"{TomlHelper.Escape(blog.Name)}\"");
                sb.AppendLine($"url = \"{TomlHelper.Escape(blog.Url)}\"");
                sb.AppendLine($"rss_url = \"{TomlHelper.Escape(blog.RssUrl)}\"");
                sb.AppendLine($"status = \"{blog.Status}\"");
                sb.AppendLine($"submitted_at = \"{blog.SubmittedAt}\"");
                if (!string.IsNullOrEmpty(blog.ApprovedAt))
                    sb.AppendLine($"approved_at = \"{blog.ApprovedAt}\"");
                if (!string.IsNullOrEmpty(blog.Description))
                    sb.AppendLine($"description = \"{TomlHelper.Escape(blog.Description)}\"");
                sb.AppendLine();
            }

            File.WriteAllText(_blogsPath, sb.ToString());
            InvalidateCache();
        }
    }

    private void InvalidateCache()
    {
        _cache.Remove(CacheKeyAll);
        _cache.Remove(CacheKeyApproved);
    }

    private Blog MapToDomain(BlogConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Url = c.Url,
        RssUrl = c.RssUrl,
        Status = Enum.Parse<BlogStatus>(c.Status),
        SubmittedAt = DateTime.Parse(c.SubmittedAt),
        ApprovedAt = c.ApprovedAt != null ? DateTime.Parse(c.ApprovedAt) : null,
        Description = c.Description
    };

    // ===== 公开查询 =====

    public List<Blog> GetAll()
    {
        return ReadConfig().Blogs.Select(MapToDomain).ToList();
    }

    public List<Blog> GetApproved()
    {
        var cached = _cache.Get<List<Blog>>(CacheKeyApproved);
        if (cached != null) return cached;

        var blogs = GetAll().Where(b => b.Status == BlogStatus.Approved).ToList();
        _cache.Set(CacheKeyApproved, blogs, CacheDuration);
        return blogs;
    }

    public List<Blog> GetPending()
    {
        return GetAll().Where(b => b.Status == BlogStatus.Pending).ToList();
    }

    public Blog? GetById(string id)
    {
        return GetAll().FirstOrDefault(b => b.Id == id);
    }

    public bool ExistsId(string id)
    {
        return ReadConfig().Blogs.Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(Blog blog)
    {
        var config = ReadConfig();
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
        WriteConfig(config);
    }

    public void UpdateStatus(string blogId, BlogStatus status)
    {
        var config = ReadConfig();
        var blog = config.Blogs.FirstOrDefault(b => b.Id == blogId);
        if (blog != null)
        {
            blog.Status = status.ToString();
            if (status == BlogStatus.Approved)
                blog.ApprovedAt = DateTime.UtcNow.ToString("O");
            WriteConfig(config);
        }
    }

    public List<Blog> GetRandom(int count = 5)
    {
        var blogs = GetApproved();
        if (blogs.Count == 0) return [];
        return blogs.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }

    public void Delete(string blogId)
    {
        var config = ReadConfig();
        var removed = config.Blogs.RemoveAll(b => b.Id == blogId);
        if (removed > 0)
        {
            WriteConfig(config);
            _logger.LogInformation("Deleted blog {BlogId}", blogId);
        }
    }
}
