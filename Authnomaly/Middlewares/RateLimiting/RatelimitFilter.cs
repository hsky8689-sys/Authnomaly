using System.Net;
using System.Threading.RateLimiting;
using Authnomaly.Domain;
using Authnomaly.Repositories;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;
using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;

public class RateLimitResponse
{
    private bool _accepted;
    private int _remainingTokens;
    private long? _retryAfter;
    public bool Accepted => _accepted;
    public long? RetryAfter => _retryAfter;
    public int RemainingTokens => _remainingTokens;
    public RateLimitResponse(bool accepted,int remainingTokens,long retryAfter)
    {
        _accepted = accepted;
        _retryAfter = retryAfter;
        _remainingTokens = remainingTokens;
    }
}
public class RatelimitFilter
{
    private readonly ILogger<RatelimitFilter> _logger;
    private readonly RequestDelegate _next;
    private static IReadOnlyLimitKeysRepo _limitDataRepository;
    private readonly IConnectionMultiplexer _multiplexer;
    private static readonly string _dbRateLimitKey = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_RATELIMIT_KEY")!;
    private readonly AuthnomalyDatabaseContext _context;
    private static readonly PartitionedRateLimiter<(string Key, RateLimitStrategy Strategy, EndpointLimitData? Data)> _fallbackLimiter = PartitionedRateLimiter.Create<(string Key, RateLimitStrategy Strategy, EndpointLimitData? Data), string>(r=> RatelimitUtils.MakeFallbackRateLimiter(r.Strategy, r.Key, r.Data));
    public RatelimitFilter(RequestDelegate next,
                          IConnectionMultiplexer multiplexer,
                          IReadOnlyLimitKeysRepo repo,
                          AuthnomalyDatabaseContext context,
                          ILogger<RatelimitFilter> logger)
    {
        _next = next;
        _multiplexer = multiplexer;
        _limitDataRepository = repo;
        _context = context;
        _logger = logger;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        HttpMethod method=HttpMethod.Post;
        string url="";
        RateLimitResponse response = new RateLimitResponse(false, -1, -1);
        _logger.LogDebug($"Limiter trace identifier:{context.TraceIdentifier},Response started stage:{context.Response.HasStarted}");
        try
        {
            context.Request.EnableBuffering();
            method = HttpMethod.Parse(context.Request.Method);
            url = context.Request.Path.Value!; //!
            IList<EndpointLimitData> found = await _limitDataRepository.FindRulesOrdered(method, url);
            foreach (EndpointLimitData data in found)
            {
                IRateLimitter limiter = RatelimitUtils.MakeRateLimiter(data.Strategy, _multiplexer);
                string key = await RatelimitUtils.CreateCounterCacheKeyAfterEntity(data, context);
                response = await limiter.IncrementKey(key, TimeSpan.FromSeconds(data.WindowSeconds), data.Limit);
                if (!response.Accepted)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests; 
                    context.Response.Headers.RetryAfter = response.RetryAfter.ToString();
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "Too many requests",
                        retryAfterSeconds = response.RetryAfter,
                        accepted = response.Accepted,
                        remainingTokens = response.RemainingTokens
                    }); 
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            try
            {
                IRateLimitKeysRepo dbLimitDataRepo = new EndpointLimitDataRepository(_context, _multiplexer);
                RateLimitPartition<string> dbLimit = RatelimitUtils.MakeFallbackRateLimiter(RateLimitStrategy.FixedWindow, _dbRateLimitKey);

                var orderedByPriority = await dbLimitDataRepo.FindRulesOrdered(method, url);
                IList<RateLimitPartition<string>> partitions = new List<RateLimitPartition<string>>();
                foreach (EndpointLimitData data in orderedByPriority)
                {
                    string key = $"rl:{data.Id}:{context.Connection?.RemoteIpAddress?.ToString()}";
                    //RateLimitPartition<string> limiter = RatelimitUtils.MakeFallbackRateLimiter(data.Strategy, key, data);
                }
                //TODO rate limit in memory  + leaky bucket implementation
            }
            catch (Exception e)
            {
                return;
            }
        }
        await _next(context);
    }
}