using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

//Used for APIs that allow delays(do not need immediate allow/deny responses)
public class LeakyBucketShapingRateLimiter:IRateLimitter
{
    private const string LuaScript = @"
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local leak_rate = tonumber(ARGV[2])
        local now = redis.call('TIME') --tonumber(ARGV[3])
        
        local availible = redis.call('HGETALL',key)
        local next_free = now
        
        if #availible > 0 then
            local fields = {}
            for i=1, #data, 2 do
                fields[availible[i]] = availible[i+1]
            end
            next_free = tonumber(fields['next_free']) or now
        end
        
        if next_free<now then
            next_free = now
        end
        
        local delay = next_free - now
        local queue_depth = delay * leak_rate
        
        local allowed = 0
        local remaining = math.max(0,math.floor(capacity-queue_depth))
        local delay_ms = 0
        
        if queue_depth + 1 <= capacity then
            delay_ms = math.floor(delay*1000)
            next_free = next_free + (1/leak_rate)
            allowed = 1
            queue_depth = queue_depth + 1
            remaining = math.max(0,math.floor(capacity - queue_depth))
        end
        
        redis.call('HSET',key,'next_free',tostring(next_free))
        redis.call('EXPIRE',key,math.ceil(capacity/leak_rate)+1)        

        return {allowed,remaining,delay_ms}
    ";
    private IDatabase _db;
    public LeakyBucketShapingRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<RateLimitResponse> IncrementKey(string key, TimeSpan ttl, int limit)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,limit/ttl.TotalSeconds,timestamp}
            );
        bool accepted = Convert.ToBoolean(call[0]);
        int tokensRemaining = Convert.ToInt32(call[1]);
        long retryAfter = Convert.ToInt64(call[2]);
        return new RateLimitResponse(accepted,tokensRemaining,retryAfter);
    }
}
//good for API-s where immediate allow/deny responses are required
public class LeakyBucketPolicingRateLimiter : IRateLimitter
{
    private const string LuaScript = @"
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local leak_rate = tonumber(ARGV[2]) -- in our EndpointLimitData,the equivalent of 'leak rate' would be the window for the leaky bucket implementations
        local now = tonumber(ARGV[3])        

        local availible = redis.call('HGETALL',key)
        local last_level = 0
        local last_leak = now

        if #availible > 0 then
            local fields = {}
            for i = 1, #availible, 2 do
                fields[availible[i]] = availible[i+1]
            end
            last_level = tonumber(fields['level']) or 0
            last_leak = tonumber(fields['last_leak']) or now
        end
        
        local elapsed = math.max(0,now - last_leak)
        local leaked = elapsed * leak_rate
        local level = math.max(0,last_level-leaked)

        local allowed = 0
        local retry_after = 0

        if level + 1 <= capacity then
            level = level + 1
            allowed = 1
        else
            retry_after = math.ceil((level+1-capacity)/leak_rate)
        end
        local remaining = math.max(0,math.floor(capacity-level))

        -- save the leaked level in both cases, otherwise denied requests throw away what leaked
        redis.call('HSET',key,'level',tostring(level),'last_leak',tostring(now))
        redis.call('EXPIRE',key,math.ceil(capacity/leak_rate)+1)

        return {allowed,remaining,retry_after}
    ";
    private IDatabase _db;
    public LeakyBucketPolicingRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<RateLimitResponse> IncrementKey(string key, TimeSpan ttl, int limit)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,limit/ttl.TotalSeconds,timestamp}
            );
        bool accepted = Convert.ToBoolean(call[0]);
        int tokensRemaining = Convert.ToInt32(call[1]);
        long retryAfter = Convert.ToInt64(call[2]);
        return new RateLimitResponse(accepted,tokensRemaining,retryAfter);
    }
}