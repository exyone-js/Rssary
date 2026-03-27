using Microsoft.Extensions.Caching.Memory;

namespace BlogSwarm.Services;

public interface ICacheService
{
    T? Get<T>(string key);
    void Set<T>(string key, T value, TimeSpan? expiration = null);
    void Remove(string key);
    void Clear();
}

public class MemoryCacheService : ICacheService, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(30);
    private readonly HashSet<string> _keys = new();
    private readonly object _keysLock = new();

    public MemoryCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public T? Get<T>(string key)
    {
        return _cache.TryGetValue(key, out T? value) ? value : default;
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? _defaultExpiration,
            SlidingExpiration = TimeSpan.FromMinutes(10)
        };

        lock (_keysLock)
        {
            _keys.Add(key);
        }

        _cache.Set(key, value, options);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        lock (_keysLock)
        {
            _keys.Remove(key);
        }
    }

    public void Clear()
    {
        lock (_keysLock)
        {
            foreach (var key in _keys.ToList())
            {
                _cache.Remove(key);
            }
            _keys.Clear();
        }
    }

    public void Dispose()
    {
        Clear();
    }
}
