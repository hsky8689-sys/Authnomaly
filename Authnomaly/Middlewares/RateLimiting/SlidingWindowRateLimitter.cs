using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class SlidingWindowRateLimitter : IRateLimitter
{
    private const string LuaScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local now = tonumber(redis.call('TIME'))
        local window = tonumber(ARGV[2])
        local member = ARGV[3]
        local lastCleared = now-window
        redis.call('ZREMRANGEBYSCORE',key,'-inf',lastCleared)
        local current = tonumber(redis.call('ZCARD',key))
        if current < limit then
            redis.call('ZADD',key,now,member)
            redis.call('EXPIRE',key,window,'NX')
            return 1
        end
        return 0
    ";
    private readonly IDatabase _db;
    public SlidingWindowRateLimitter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<bool> IncrementKey(string key,TimeSpan ttl,int limit)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,ttl.TotalSeconds,timestamp}
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