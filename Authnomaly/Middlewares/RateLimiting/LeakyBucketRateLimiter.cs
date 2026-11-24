using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

//Used for APIs that allow delays(do not need immediate allow/deny responses)
public class LeakyBucketShapingRateLimiter:IRateLimitter
{
    private const string LuaScript = @"""
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local leak_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        
        local availible = redis.call('HGETALL',key)
        local next_free = now
        
        if #data > 0 then
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
            next_free = next_free + (1/leak+rate)
            allowed = 1
            queue_depth = queue_depth + 1
            remaining = math.max(0,math.floor(capacity - queue_depth))
        end
        
        redis.call('HSET',key,'next_free',tostring(next_free))
        redis.call('EXPIRE',key,math.ceil(capacity/leak_rate)+1)

        return {allowed,remaining,delay_ms}
    """;
    private IDatabase _db;
    public LeakyBucketShapingRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<RateLimitResponse> IncrementKey(string key, TimeSpan ttl, int limit)
    {
        try
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,ttl.TotalSeconds,timestamp}
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
//good for API-s where immediate allow/deny responses are required
public class LeakyBucketPolicingRateLimiter : IRateLimitter
{
    private const string LuaScript = @"""
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local leak_rate = tonumber(ARGV[2]) -- in our EndpointLimitData,the equivalent of 'leak rate' would be the window for the leaky bucket implementations
        local now = tonumber(ARGV[3])        

        local availible = redis.call('HGETALL',key)
        local last_level = 0
        local last_leak = now

        if #data > 0 then
            local fields = {}
            for i = 1, #data, 2 do
                fields[availible[i]] = availible[i+1]
            end
            level = tonumber(fields['level']) or 0
            last_leak = tonumber(fields['last_leak']) or now
        end
        
        local elapsed = now - last_leak
        local leaked = elapsed * leak_rate
        level = math.max(0,level-leaked)

        local allowed = 0
        local remaining = math.max(0,math.floor(capacity-level))

        if level + 1 <= capacity then
            level = level + 1
            remaining = math.max(0,math.floor(capacity-level))
            allowed = 1
        end

        redis.call('HSET',key,'level',tostring(level),'last_leak',tostring(now))
        redis.call('EXPIRE',ley,math.ceil(capacity/leak_rate)+1)
        
        return {allowed,remaining,0}            
    """;
    private IDatabase _db;
    public LeakyBucketPolicingRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<RateLimitResponse> IncrementKey(string key, TimeSpan ttl, int limit)
    {
        try
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,ttl.TotalSeconds,timestamp}
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