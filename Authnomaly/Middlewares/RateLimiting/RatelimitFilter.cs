using System.Net;
using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using StackExchange.Redis;
using System.Threading.RateLimiting;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Utils;
using Microsoft.AspNetCore.RateLimiting;

namespace Authnomaly.Middlewares.RateLimiting;

public class RateLimitResponse
{
    private bool _accepted;
    private long? _retryAfter;
    public bool Accepted => _accepted;
    public long? RetryAfter => _retryAfter;
    public RateLimitResponse()
    {
        
    }
}
public class RatelimitFilter
{
    private readonly RequestDelegate _next;
    private static IReadOnlyLimitKeysRepo _limitDataRepository;
    private readonly IConnectionMultiplexer _multiplexer;
    private static readonly string _dbRateLimitKey = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_RATELIMIT_KEY")!;
    private static readonly PartitionedRateLimiter<(string Key, RateLimitStrategy Strategy, EndpointLimitData? Data)> _fallbackLimiter = PartitionedRateLimiter.Create<(string Key, RateLimitStrategy Strategy, EndpointLimitData? Data), string>(r=> RatelimitUtils.MakeFallbackRateLimiter(r.Strategy, r.Key, r.Data));
    public RatelimitFilter(RequestDelegate next,
                          IConnectionMultiplexer multiplexer,
                          IReadOnlyLimitKeysRepo repo)
    {
        _next = next;
        _multiplexer = multiplexer;
        _limitDataRepository = repo;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        HttpMethod method=HttpMethod.Post;
        string url="";
        try
        {
            context.Request.EnableBuffering();
            method = HttpMethod.Parse(context.Request.Method);
            url = context.Request.Path.Value!; //!
            IList<EndpointLimitData> found = await _limitDataRepository.FindRulesOrdered(method, url);
            foreach (EndpointLimitData data in found)
            {
                IRateLimitter limiter = RatelimitUtils.MakeRateLimiter(data.Strategy,_multiplexer);
                string key = $"rl:{data.Id}:{context.Connection?.RemoteIpAddress?.ToString()}";
                if (!await limiter.IncrementKey(key, TimeSpan.FromSeconds(data.WindowSeconds), data.Limit))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            IRateLimitKeysRepo dbLimitDataRepo = new EndpointLimitDataRepository(null,_multiplexer);
            RateLimitPartition<string> dbLimit = RatelimitUtils.MakeFallbackRateLimiter(RateLimitStrategy.FixedWindow,_dbRateLimitKey);
            var orderedByPriority = await dbLimitDataRepo.FindRulesOrdered(method, url);
            IList<RateLimitPartition<string>> partitions = new List<RateLimitPartition<string>>();
            foreach (EndpointLimitData data in orderedByPriority)
            {
                string key = $"rl:{data.Id}:{context.Connection?.RemoteIpAddress?.ToString()}";
                RateLimitPartition<string> limiter = RatelimitUtils.MakeFallbackRateLimiter(data.Strategy,key,data);
            }
            //TODO rate limit in memory + key after entity + leaky bucket implementation
        }
        await _next(context);
    }
}