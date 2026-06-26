using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.ServiceModel.Syndication;
using Rssary.DataStore;
using Rssary.DomainModels;

namespace Rssary.ApiEndpoints;

/// <summary>
/// OPML 导入/导出 API
/// </summary>
public static class OpmlEndpoints
{
    public static void MapOpmlEndpoints(this WebApplication app)
    {
        // ===== OPML 导出 =====
        app.MapGet("/api/opml/export", (BlogStore blogs) =>
        {
            var approvedBlogs = blogs.GetApproved();

            var opml = new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", "Rssary Subscriptions")
                ),
                new XElement("body",
                    approvedBlogs.Select(b =>
                        new XElement("outline",
                            new XAttribute("type", "rss"),
                            new XAttribute("text", b.Name),
                            new XAttribute("title", b.Name),
                            new XAttribute("xmlUrl", b.RssUrl),
                            new XAttribute("htmlUrl", b.Url),
                            !string.IsNullOrEmpty(b.Description)
                                ? new XAttribute("description", b.Description)
                                : null!
                        )
                    )
                )
            );

            // Remove null attributes
            opml.Descendants().Attributes().Where(a => a.Value == null).Remove();

            var sb = new StringBuilder();
            using var xw = XmlWriter.Create(sb, new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            });
            opml.WriteTo(xw);
            xw.Flush();

            return Results.Text(sb.ToString(), "text/xml", Encoding.UTF8);
        }).CacheOutput("RssPolicy");

        // ===== OPML 导入 =====
        app.MapPost("/api/opml/import", async (HttpRequest request,
            BlogStore blogs, ArticleStore articles, RssReading.RssFeedReader reader) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { Error = "Expected multipart/form-data with file field 'opml'" });

            var file = request.Form.Files.GetFile("opml");
            if (file == null || file.Length == 0)
                return Results.BadRequest(new { Error = "No OPML file provided" });

            using var stream = file.OpenReadStream();
            using var reader2 = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            var doc = XDocument.Load(reader2);
            var ns = doc.Root?.Name.Namespace;

            var outlines = doc.Descendants()
                .Where(e => e.Name.LocalName == "outline"
                    && (e.Attribute("type")?.Value == "rss"
                        || (e.Attribute("xmlUrl") != null
                            && (e.Attribute("type") == null || e.Attribute("type")!.Value == ""))));

            var imported = 0;
            var skipped = 0;
            var errors = new List<string>();

            foreach (var outline in outlines)
            {
                try
                {
                    var rssUrl = outline.Attribute("xmlUrl")?.Value;
                    var name = outline.Attribute("title")?.Value
                               ?? outline.Attribute("text")?.Value
                               ?? "Untitled";
                    var url = outline.Attribute("htmlUrl")?.Value ?? rssUrl;
                    var desc = outline.Attribute("description")?.Value;

                    if (string.IsNullOrWhiteSpace(rssUrl))
                    {
                        skipped++;
                        continue;
                    }

                    // Generate a unique ID from the RSS URL
                    var id = GenerateId(rssUrl);
                    if (blogs.ExistsId(id) || blogs.GetAll().Any(b => b.RssUrl == rssUrl))
                    {
                        skipped++;
                        continue;
                    }

                    // Validate RSS URL
                    var isValid = await reader.ValidateRssUrlAsync(rssUrl);
                    if (!isValid)
                    {
                        errors.Add($"Invalid RSS: {rssUrl}");
                        skipped++;
                        continue;
                    }

                    var blog = new Blog
                    {
                        Id = id,
                        Name = name.Length > 100 ? name[..100] : name,
                        Url = url ?? rssUrl,
                        RssUrl = rssUrl,
                        Description = desc?.Length > 500 ? desc[..500] : desc,
                        Status = BlogStatus.Pending,
                        SubmittedAt = DateTime.UtcNow
                    };

                    blogs.Add(blog);
                    imported++;
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                    skipped++;
                }
            }

            return Results.Ok(new
            {
                Imported = imported,
                Skipped = skipped,
                Errors = errors.Count > 0 ? errors : null
            });
        });
    }

    private static string GenerateId(string rssUrl)
    {
        // Use last segment of URL as base, sanitize to alphanumeric
        var uri = new Uri(rssUrl);
        var host = uri.Host.Replace("www.", "").Replace(".", "-");
        var path = uri.Segments.LastOrDefault()?.Replace(".", "-") ?? "feed";
        var raw = $"{host}-{path}";
        // Keep only a-z0-9 and hyphens, max 30 chars
        var sanitized = new string(raw.ToLowerInvariant()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            .Take(30).ToArray()).Trim('-');
        return sanitized.Length < 3 ? sanitized.PadRight(3, 'x') : sanitized;
    }
}
