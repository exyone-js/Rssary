using Rssary.DataStore;

namespace Rssary.ApiEndpoints;

/// <summary>
/// 站点辅助 API：随机博主、随机名言
/// </summary>
public static class SiteEndpoints
{
    public static void MapSiteEndpoints(this WebApplication app)
    {
        // 随机获取 N 个已审核博主
        app.MapGet("/api/random-blogs", (BlogStore blogs, int count = 5) =>
        {
            return Results.Ok(blogs.GetRandom(count).Select(b => new
            {
                b.Id, b.Name, b.Description
            }));
        });

        // 随机获取一条名言
        app.MapGet("/api/random-quote", (QuoteStore quotes) =>
        {
            var q = quotes.GetRandom();
            if (q == null)
                return Results.Ok(new { Text = "", Author = "" });
            return Results.Ok(new { q.Text, q.Author });
        });
    }
}
