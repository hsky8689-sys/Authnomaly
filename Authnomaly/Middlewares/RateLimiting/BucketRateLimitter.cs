using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class BucketRateLimitter : IRateLimitter
{
    private static string LuaScript = @"
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
            Console.WriteLine($"Key:{key}\n,ttl:{ttl.TotalSeconds},limit:{limit}");
            var call = await _db.ScriptEvaluateAsync(
                LuaScript, 
                new RedisKey[]{key},
                new RedisValue[]{limit,ttl.TotalSeconds}
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