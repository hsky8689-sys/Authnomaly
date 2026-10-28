using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class LeakyBucketRateLimiter:IRateLimitter
{
    private const string LuaScript = @"";
    private IDatabase _db;
    public LeakyBucketRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public Task<bool> IncrementKey(string key, TimeSpan ttl, int limit)
    {
        throw new NotImplementedException();
    }
}