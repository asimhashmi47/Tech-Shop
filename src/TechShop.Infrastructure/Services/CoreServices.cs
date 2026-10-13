using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using TechShop.Application.Common.Interfaces;

namespace TechShop.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    // 11 keeps sign-in responsive on a shop PC while staying well above the default cost.
    private const int WorkFactor = 11;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

/// <summary>
/// IMemoryCache wrapper with logical groups. Keys are tracked per group so one write can
/// drop every related entry, for example all dashboard aggregates after a sale.
/// </summary>
public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groups = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        if (cache.TryGetValue(key, out var cached) && cached is T hit)
        {
            return hit;
        }

        var value = await factory().ConfigureAwait(false);
        cache.Set(key, value, ttl ?? DefaultTtl);
        Track(key);
        return value;
    }

    public void Remove(string key)
    {
        cache.Remove(key);
        foreach (var group in _groups.Values)
        {
            group.TryRemove(key, out _);
        }
    }

    public void RemoveGroup(string group)
    {
        if (!_groups.TryRemove(group, out var keys))
        {
            return;
        }

        foreach (var key in keys.Keys)
        {
            cache.Remove(key);
        }
    }

    private void Track(string key)
    {
        var group = key.Split(':', 2)[0];
        _groups.GetOrAdd(group, _ => new ConcurrentDictionary<string, byte>())[key] = 0;
    }
}

