namespace BlogSwarm.Models;

public class Blog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public BlogStatus Status { get; set; } = BlogStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public string? Description { get; set; }
}

public enum BlogStatus
{
    Pending,
    Approved,
    Rejected
}
