using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class FixedWindowRateLimitter : IRateLimitter
{
    private readonly IDatabase _db;
    public FixedWindowRateLimitter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<bool> IncrementKey(string key,TimeSpan ttl,int limit)
    {
        /*Any key is decremented each time a request is
         performed at the respective key(handled by middleware)
         MULTI
         INCR key
         EXPIRE key NX ttl
         EXEC
         */
        var transaction = _db.CreateTransaction();
        var incr = _db.StringIncrementAsync(key);
        var expire = _db.KeyExpireAsync(key,ttl,ExpireWhen.HasNoExpiry);
        await transaction.ExecuteAsync();
        return await incr <= limit;
    }
}