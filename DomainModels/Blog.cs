using System.Text.RegularExpressions;

namespace Rssary.DomainModels;

public class Blog
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public BlogStatus Status { get; set; } = BlogStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// 验证ID格式（只允许小写字母和数字，长度3-30）
    /// </summary>
    public static bool IsValidId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length < 3 || id.Length > 30) return false;
        return Regex.IsMatch(id, @"^[a-z0-9]+$");
    }
}

public enum BlogStatus
{
    Pending,
    Approved,
    Rejected
}
