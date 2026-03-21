using BlogSwarm.Models;
using BlogSwarm.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogSwarm.Pages;

public class BlogModel : PageModel
{
    private readonly DataService _dataService;

    public Blog? Blog { get; set; }
    public List<Article> Articles { get; set; } = [];
    public bool BlogNotFound { get; set; } = false;

    private const int PageSize = 20;

    public BlogModel(DataService dataService)
    {
        _dataService = dataService;
    }

    public void OnGet(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            BlogNotFound = true;
            return;
        }

        Blog = _dataService.GetBlogById(id);

        if (Blog == null || Blog.Status != BlogStatus.Approved)
        {
            BlogNotFound = true;
            return;
        }

        Articles = _dataService.GetArticlesByBlogId(id, 1, PageSize);
    }
}
