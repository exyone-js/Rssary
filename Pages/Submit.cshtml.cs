using BlogSwarm.Models;
using BlogSwarm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogSwarm.Pages;

public class SubmitModel : PageModel
{
    private readonly DataService _dataService;
    private readonly RssService _rssService;

    [BindProperty]
    public BlogSubmission Input { get; set; } = new();

    public string? Message { get; set; }
    public bool IsSuccess { get; set; }

    public SubmitModel(DataService dataService, RssService rssService)
    {
        _dataService = dataService;
        _rssService = rssService;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var isValidRss = await _rssService.ValidateRssUrlAsync(Input.RssUrl);
        if (!isValidRss)
        {
            ModelState.AddModelError("Input.RssUrl", "RSS 链接无效或无法访问");
            return Page();
        }

        var existingBlogs = _dataService.GetBlogs();
        if (existingBlogs.Any(b => b.RssUrl == Input.RssUrl || b.Url == Input.Url))
        {
            ModelState.AddModelError("", "该博客已存在");
            return Page();
        }

        var blog = new Blog
        {
            Name = Input.Name,
            Url = Input.Url,
            RssUrl = Input.RssUrl,
            Description = Input.Description,
            Status = BlogStatus.Pending,
            SubmittedAt = DateTime.UtcNow
        };

        _dataService.AddBlog(blog);
        Message = "博客提交成功，等待审核";
        IsSuccess = true;
        Input = new BlogSubmission();

        return Page();
    }
}

public class BlogSubmission
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
}
