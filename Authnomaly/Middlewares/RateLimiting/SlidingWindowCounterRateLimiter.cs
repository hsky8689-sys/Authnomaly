using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class SlidingWindowCounterRateLimiter:IRateLimitter
{
    private static string LuaScript = @"
        local current_key = KEYS[1]
        local last_key = KEYS[2]
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local delta = tonumber(ARGV[3])
        local last_counted = tonumber(redis.call('GET',last_key) or '0') or 0
        local current_counted = tonumber(redis.call('GET',current_key) or '0') or 0
        local bias = last_counted * (1-delta)
        local estimated = bias + current_counted
        if estimated >= limit then
            return 0
        end
        local new_value = redis.call('INCR',current_key)
        if new_value == 1 then
            redis.call('EXPIRE',current_key,window*2)
        end
        return 1
    "; 
    private readonly IDatabase _db;
    public SlidingWindowCounterRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    } 
    public async Task<bool> IncrementKey(string key, TimeSpan ttl, int limit)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var now = timestamp / 1000;
            String currentKey = $"{key}:{now}";
            String lastKey = $"{key}:{now/ttl.TotalSeconds}";
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{currentKey,lastKey},
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