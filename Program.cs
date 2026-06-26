using Rssary.Caching;
using Rssary.DataStore;
using Rssary.RssReading;
using Rssary.ApiEndpoints;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "WebAssets"
});

// ===== 基础设施 =====
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICache, MemoryCache>();

// ===== CORS（公开 API 访问） =====
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ===== 数据存储 =====
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var storeDir = Path.Combine(env.ContentRootPath, "AppStorage");
    return new BlogStore(
        Path.Combine(storeDir, "blogs.toml"),
        sp.GetRequiredService<ICache>(),
        sp.GetRequiredService<ILogger<BlogStore>>());
});

builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var storeDir = Path.Combine(env.ContentRootPath, "AppStorage");
    var articlesDir = Path.Combine(storeDir, "Articles");
    return new ArticleStore(
        articlesDir,
        sp.GetRequiredService<ICache>(),
        sp.GetRequiredService<ILogger<ArticleStore>>());
});

builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var configPath = Path.Combine(env.ContentRootPath, "AppStorage", "config.toml");
    return new AppConfigManager(configPath, sp.GetRequiredService<ILogger<AppConfigManager>>());
});

builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var quotesPath = Path.Combine(env.ContentRootPath, "AppStorage", "quotes.toml");
    return new QuoteStore(quotesPath);
});

builder.Services.AddSingleton<SiteQueries>();

builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var i18nPath = Path.Combine(env.ContentRootPath, "AppStorage", "i18n.toml");
    return new I18NStore(i18nPath);
});

// ===== RSS 读取 =====
builder.Services.AddSingleton<RssFeedReader>();
builder.Services.AddHostedService<FeedSyncJob>();

// ===== 缓存策略 =====
builder.Services.AddResponseCaching();
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(b => b.Expire(TimeSpan.FromMinutes(5)));
    options.AddPolicy("ApiPolicy", b => b.Expire(TimeSpan.FromMinutes(10)));
    options.AddPolicy("RssPolicy", b => b.Expire(TimeSpan.FromMinutes(30)));
});

var app = builder.Build();

// ===== 中间件 =====
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

// ===== 首次运行检测（必须在 UseStaticFiles 之前，否则静态文件直接响应，中间件无法拦截） =====
var hostEnv = app.Services.GetRequiredService<IWebHostEnvironment>();
var configFilePath = Path.Combine(hostEnv.ContentRootPath, "AppStorage", "config.toml");

app.Use(async (ctx, next) =>
{
    var isSetupMode = !File.Exists(configFilePath);

    // 文件存在但 review_key_hash 尚未设置，也视为未初始化
    if (!isSetupMode)
    {
        try
        {
            var content = File.ReadAllText(configFilePath);
            if (!content.Contains("review_key_hash"))
                isSetupMode = true;
        }
        catch
        {
            isSetupMode = true;
        }
    }

    if (isSetupMode)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/setup", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/setup.html", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/setup.html");
            return;
        }
    }
    await next();
});

app.UseCors();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        else if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=3600");
        else
            ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000");
    }
});

app.UseRouting();
app.UseResponseCaching();
app.UseOutputCache();

// ===== 创建目录（确保正常模式目录存在） =====
var storeDir = Path.Combine(hostEnv.ContentRootPath, "AppStorage");
var articlesDir = Path.Combine(storeDir, "Articles");
Directory.CreateDirectory(storeDir);
Directory.CreateDirectory(articlesDir);

// ===== 页面路由 =====
app.MapGet("/", () => Results.Redirect("/index.html"));

// ===== API 端点 =====
app.MapGet("/api/setup", (AppConfigManager cfg,
                          string reviewKey,
                          string? siteTitle,
                          string? siteDescription,
                          string? siteLanguage) =>
{
    return SetupRssary(hostEnv, cfg, reviewKey, siteTitle, siteDescription, siteLanguage);
});

app.MapPost("/api/setup", async (HttpRequest request,
                                 AppConfigManager cfg) =>
{
    var body = await request.ReadFromJsonAsync<SetupRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.ReviewKey))
        return Results.BadRequest(new { error = "Admin key is required" });

    return SetupRssary(hostEnv, cfg, body.ReviewKey, body.SiteTitle, body.SiteDescription, body.SiteLanguage);
});

app.MapFeedEndpoint();
app.MapBlogEndpoints();
app.MapArticleEndpoints();
app.MapReviewEndpoints();
app.MapSiteEndpoints();
app.MapPublicApiEndpoints();
app.MapOpmlEndpoints();

app.Run();

// ===== 初始化逻辑 =====
static IResult SetupRssary(IWebHostEnvironment env,
                           AppConfigManager cfg,
                           string reviewKey,
                           string? siteTitle,
                           string? siteDescription,
                           string? siteLanguage)
{
    try
    {
        var storeDir = Path.Combine(env.ContentRootPath, "AppStorage");
        var articlesDir = Path.Combine(storeDir, "Articles");
        Directory.CreateDirectory(storeDir);
        Directory.CreateDirectory(articlesDir);

        if (!string.IsNullOrWhiteSpace(reviewKey) && reviewKey.Length >= 6)
            cfg.SetReviewKey(reviewKey);
        if (!string.IsNullOrWhiteSpace(siteTitle))
            cfg.SetSiteTitle(siteTitle);
        if (siteDescription != null)
            cfg.SetSiteDescription(siteDescription);
        if (!string.IsNullOrWhiteSpace(siteLanguage))
            cfg.SetSiteLanguage(siteLanguage);

        return Results.Ok(new { message = "Rssary initialized successfully" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}
