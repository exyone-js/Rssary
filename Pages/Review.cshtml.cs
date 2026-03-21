using BlogSwarm.Models;
using BlogSwarm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogSwarm.Pages;

public class ReviewModel : PageModel
{
    private readonly DataService _dataService;
    private readonly RssService _rssService;

    public List<Blog> PendingBlogs { get; set; } = [];
    public bool IsAuthenticated { get; set; }
    public string? ErrorMessage { get; set; }

    public ReviewModel(DataService dataService, RssService rssService)
    {
        _dataService = dataService;
        _rssService = rssService;
    }

    public IActionResult OnGet(string? key)
    {
        var reviewKey = _dataService.GetReviewKey();
        
        if (string.IsNullOrEmpty(key) || key != reviewKey)
        {
            IsAuthenticated = false;
            return Page();
        }

        IsAuthenticated = true;
        PendingBlogs = _dataService.GetPendingBlogs();
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(string blogId, string key)
    {
        var reviewKey = _dataService.GetReviewKey();
        if (key != reviewKey)
        {
            return RedirectToPage();
        }

        _dataService.UpdateBlogStatus(blogId, BlogStatus.Approved);

        var blog = _dataService.GetBlogById(blogId);
        if (blog != null)
        {
            var articles = await _rssService.FetchArticlesAsync(blog);
            _dataService.AddArticles(blogId, articles);
        }

        return RedirectToPage(new { key });
    }

    public IActionResult OnPostRejectAsync(string blogId, string key)
    {
        var reviewKey = _dataService.GetReviewKey();
        if (key != reviewKey)
        {
            return RedirectToPage();
        }

        _dataService.UpdateBlogStatus(blogId, BlogStatus.Rejected);
        return RedirectToPage(new { key });
    }
}
