namespace BlogSwarm.Models;

public class BlogSwarmConfig
{
    public List<BlogConfig> Blogs { get; set; } = [];
}

public class BlogConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string SubmittedAt { get; set; } = string.Empty;
    public string? ApprovedAt { get; set; }
    public string? Description { get; set; }
}

public class ArticleConfig
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PublishedAt { get; set; } = string.Empty;
    public string FetchedAt { get; set; } = string.Empty;
    public string? Author { get; set; }
}

public class ArticleFileConfig
{
    public string BlogId { get; set; } = string.Empty;
    public List<ArticleConfig> Articles { get; set; } = [];
}
