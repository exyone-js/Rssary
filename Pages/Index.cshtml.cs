using BlogSwarm.Models;
using BlogSwarm.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogSwarm.Pages;

public class IndexModel : PageModel
{
    private readonly DataService _dataService;

    public List<Article> Articles { get; set; } = [];
    public Dictionary<string, Blog> BlogMap { get; set; } = [];
    public string? SearchQuery { get; set; }
    public bool IsSearch { get; set; } = false;

    private const int PageSize = 20;

    public IndexModel(DataService dataService)
    {
        _dataService = dataService;
    }

    public void OnGet(string? q = null)
    {
        var blogs = _dataService.GetApprovedBlogs();
        BlogMap = blogs.ToDictionary(b => b.Id);

        SearchQuery = q?.Trim();
        IsSearch = !string.IsNullOrEmpty(SearchQuery);

        if (IsSearch && !string.IsNullOrEmpty(SearchQuery))
        {
            var searchResults = _dataService.SearchArticles(SearchQuery);
            Articles = searchResults.Take(PageSize).ToList();
        }
        else
        {
            Articles = _dataService.GetRandomArticles(PageSize);
        }
    }
}
