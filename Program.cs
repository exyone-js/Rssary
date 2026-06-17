using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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

// ===== 数据存储 =====
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var storeDir = Path.Combine(env.ContentRootPath, "AppStorage");
    var blogsDir = Path.Combine(storeDir, "Blogs");
    Directory.CreateDirectory(storeDir);
    Directory.CreateDirectory(blogsDir);

    return new BlogStore(
        Path.Combine(storeDir, "blogs.toml"),
        sp.GetRequiredService<ICache>(),
        sp.GetRequiredService<ILogger<BlogStore>>());
});

builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var blogsDir = Path.Combine(env.ContentRootPath, "AppStorage", "Blogs");
    return new ArticleStore(
        blogsDir,
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

// ===== RSS 读取 =====
builder.Services.AddSingleton<RssFeedReader>();
builder.Services.AddHostedService<FeedSyncJob>();

// ===== JSON 序列化（兼容AOT） =====
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
});

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

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
    }
});

app.UseRouting();
app.UseResponseCaching();
app.UseOutputCache();

// ===== 页面路由 =====
app.MapGet("/", () => Results.Redirect("/index.html"));

// ===== API 端点 =====
app.MapFeedEndpoint();
app.MapBlogEndpoints();
app.MapArticleEndpoints();
app.MapReviewEndpoints();
app.MapSiteEndpoints();
app.MapPublicApiEndpoints();

app.Run();
