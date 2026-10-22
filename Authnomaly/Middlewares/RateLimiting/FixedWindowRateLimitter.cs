using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class FixedWindowRateLimitter : IRateLimitter
{
    private const string LuaScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local now = tonumber(ARGV[2])
        local expire = tonumber(ARGV[3])
        local current = tonumber(redis.call('GET', key) or '0')
        if current < limit then
            redis.call('INCR',key)
            redis.call('EXPIRE',key,expire,'NX')
            return 1
        end
        return 0
    ";
    private readonly IDatabase _db;
    public FixedWindowRateLimitter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<bool> IncrementKey(string key,TimeSpan ttl,int limit)
    {
        try
        {
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),ttl.TotalSeconds}
            );
            return Convert.ToBoolean(call);
        }
        catch(Exception e)
        {
            Console.WriteLine("rate limit exception:"+e.Message);
            return false;
        }
    }
}