namespace Rssary.DomainModels;

public class Article
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BlogId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public string? Author { get; set; }
}
