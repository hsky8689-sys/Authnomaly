using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class BucketRateLimitter : IRateLimitter
{
    private static string LuaScript = @"
        local key = KEYS[1]
        local max_tokens = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        
        local data = redis.call('HGETALL',key)
        local tokens = max_tokens
        local last_refill = now

        if #data > 0 then
            local fields = {}
            for i =1 , #data, 2 do
                fields[data[i]] = data[i+1]
            end
            tokens = tonumber(fields['tokens']) or max_tokens
            last_refill = tonumber(fields['last_refill']) or now
        end
        
        local allowed = 0
        local remaining = tokens
        
        if tokens >= 1then
            tokens = tokens-1
            remaining = tokens
            allowed = 1
        end
       
        redis.call('HSET',key,'tokens',tostring(tokens),'last_refill',tostring(now))
        redis.call('EXPIRE',key,math.ceil(max_tokens/refill_rate)+1)
    ";
    private static string LuaScript1 = @"
            key = KEYS[1]
            limit = tonumber(ARGV[1])
            window = tonumber(ARGV[2])
            time_call = redis.call('TIME')
            now = tonumber(time_call[1])
            local tokens_key = 'tokens'
            local refill_key = 'refill'
            tokens = tonumber(redis.call('HGET',key,tokens_key)) or limit
            last_refill = tonumber(redis.call('HGET',key,refill_key)) or now
            redis.log(redis.LOG_WARNING, 'now=' .. tostring(now) .. ' limit=' .. tostring(limit) .. ' last_refill=' .. tostring(last_refill))
            if now - last_refill > 0 then
                added = tointeger((now - window)/limit)
                new_tokens = min(limit,tokens+added)
                redis.call('HSET',key,tokens_key,new_tokens)
                redis.call('HSET',key,refill_key,now)
                redis.call('EXPIRE',key,window,'NX')
            end
            if tokens > 0 then
                added_since = redis.call('ZRANGE',last_refill,now)
                if added_since > 0 then
                    redis.call('HSET',key,tokens_key,tokens-1)
                    return 1
                else
                    return 0
                end
            else
                return 0
            end";
    private readonly IDatabase _db;
    public BucketRateLimitter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }
    public async Task<bool> IncrementKey(string key,TimeSpan ttl,int limit)
    {
        try
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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