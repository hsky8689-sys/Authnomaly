using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class SlidingWindowRateLimitter : IRateLimitter
{
    private const string LuaScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local now = tonumber(ARGV[2])
        local window = tonumber(ARGV[3])
        local member = ARGV[4]
        local lastCleared = now-window
        redis.call('ZREMRANGEBYSCORE',key,'-inf',lastCleared)
        local current = redis.call('ZCARD',key)
        if current < limit then
            redis.call('ZADD',key,now,member)
            redis.call('PEXPIRE',key,window)
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
                new RedisValue[]{limit,timestamp,ttl.TotalSeconds,timestamp + 1}
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