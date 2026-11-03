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
            return {1,limit-current,0}
        end
        local retryAfter = redis.call('pttl',key)
        return {0,0,retryAfter}
    ";
    private readonly IDatabase _db;
    public FixedWindowRateLimitter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<RateLimitResponse> IncrementKey(string key,TimeSpan ttl,int limit)
    {
        try
        {
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),ttl.TotalSeconds}
            );
            bool accepted = Convert.ToBoolean(call[0]);
            int tokensRemaining = Convert.ToInt32(call[1]);
            long retryAfter = Convert.ToInt64(call[2]);
            return new RateLimitResponse(accepted,tokensRemaining,retryAfter);
        }
        catch(Exception e)
        {
            Console.WriteLine("rate limit exception:"+e.Message);
            return new RateLimitResponse(false,-1,-1);
        }
    }
}