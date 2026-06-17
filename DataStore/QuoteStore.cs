using Tomlyn;
using Tomlyn.Model;

namespace Rssary.DataStore;

/// <summary>
/// quotes.toml 读取 —— 随机名言
/// </summary>
public class QuoteStore
{
    private readonly string _quotesPath;
    private readonly object _fileLock = new();

    public QuoteStore(string quotesPath)
    {
        _quotesPath = quotesPath;
    }

    private List<QuoteEntry> ReadAll()
    {
        if (!File.Exists(_quotesPath)) return [];

        lock (_fileLock)
        {
            try
            {
                var text = File.ReadAllText(_quotesPath);
                var model = Toml.ToModel(text);
                var list = new List<QuoteEntry>();

                if (model.TryGetValue("quotes", out var obj) && obj is TomlTableArray arr)
                {
                    foreach (var item in arr)
                    {
                        if (item is TomlTable t)
                        {
                            list.Add(new QuoteEntry
                            {
                                Text = TomlHelper.GetString(t, "text"),
                                Author = TomlHelper.GetStringOrNull(t, "author")
                            });
                        }
                    }
                }

                return list;
            }
            catch
            {
                return [];
            }
        }
    }

    public QuoteEntry? GetRandom()
    {
        var all = ReadAll();
        if (all.Count == 0) return null;
        return all[Random.Shared.Next(all.Count)];
    }
}

public class QuoteEntry
{
    public string Text { get; set; } = "";
    public string? Author { get; set; }
}
