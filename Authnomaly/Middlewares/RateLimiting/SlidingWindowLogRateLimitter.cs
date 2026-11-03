using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class SlidingWindowLogRateLimitter : IRateLimitter
{
    private const string LuaScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local now = redis.call('TIME')[1]
        local window = tonumber(ARGV[2])
        local member = ARGV[3]
        local lastCleared = now-window
        redis.call('ZREMRANGEBYSCORE',key,'-inf',lastCleared)
        local current = redis.call('ZCARD',key)
        if current < limit then
            redis.call('ZADD',key,now,member)
            redis.call('EXPIRE',key,window)
            return {1,limit-current,0}
        end
        local retryAfter = redis.call('PTTL',key)
        return {0,0,retryAfter}
    ";
    private readonly IDatabase _db;
    public SlidingWindowLogRateLimitter(IConnectionMultiplexer multiplexer)
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
                new RedisValue[]{limit,ttl.TotalSeconds,Guid.NewGuid().ToString()}
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