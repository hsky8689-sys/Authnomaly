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
    private IReadOnlyLimitKeysRepo _limitDataRepository;
    private IConnectionMultiplexer _multiplexer;
    private PartitionedRateLimiter<(string Key, RateLimitStrategy Strategy, EndpointLimitData? Data)>? _fallbackLimiter;
    private PartitionedRateLimiter<HttpContext>? _dbLimiter;
    public RatelimitFilter(RequestDelegate next,
                          IConnectionMultiplexer multiplexer,
                          IReadOnlyLimitKeysRepo repo,
                          ILogger<RatelimitFilter> logger)
    {
        _next = next;
        _multiplexer = multiplexer;
        _limitDataRepository = repo;
        _logger = logger;
        _fallbackLimiter =  PartitionedRateLimiter.Create<(string Key, RateLimitStrategy Strategy, EndpointLimitData? Data), string>(r => RatelimitUtils.MakeFallbackRateLimiter(r.Strategy, r.Key, r.Data));
        string dbRateLimitKey = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_RATELIMIT_KEY") ?? throw new ArgumentException("NO DATABASE RATELIMIT KEY SET");
        _dbLimiter =  PartitionedRateLimiter.Create<HttpContext, string>(_ => RatelimitUtils.MakeFallbackRateLimiter(RateLimitStrategy.FixedWindow, dbRateLimitKey));
    }
    public async Task InvokeAsync(HttpContext context,AuthnomalyDatabaseContext dbContext)
    {
        HttpMethod method=HttpMethod.Post;
        string url="";
        RateLimitResponse response = new RateLimitResponse(false, -1, -1);
        //_logger.LogDebug($"Limiter trace identifier:{context.TraceIdentifier},Response started stage:{context.Response.HasStarted}");
        IRateLimitKeysRepo dbLimitDataRepo = new EndpointLimitDataRepository(dbContext, _multiplexer);
        try
        {
            context.Request.EnableBuffering();
            method = HttpMethod.Parse(context.Request.Method);
            url = context.Request.Path.Value!; //!
            IList<EndpointLimitData> found = await _limitDataRepository.FindRulesOrdered(method, url);
            Dictionary<RateLimitStrategy, IRateLimitter> limiters = new Dictionary<RateLimitStrategy, IRateLimitter>();
            foreach (EndpointLimitData data in found)
            {
                if (!limiters.ContainsKey(data.Strategy))
                    limiters[data.Strategy] = RatelimitUtils.MakeRateLimiter(data.Strategy, _multiplexer);
                IRateLimitter limiter = limiters[data.Strategy];
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
                using var dbLease = _dbLimiter!.AttemptAcquire(context);
                if (!dbLease.IsAcquired)
                {
                    long retryAfter = dbLease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan ra)
                        ? (long)Math.Ceiling(ra.TotalSeconds)   
                        : 100;                   
                    response = new RateLimitResponse(false, 0, retryAfter);
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
                var orderedByPriority = await dbLimitDataRepo.FindRulesOrdered(method, url);
                foreach (EndpointLimitData data in orderedByPriority)
                {
                    string key = await RatelimitUtils.CreateCounterCacheKeyAfterEntity(data, context);
                    using var lease = _fallbackLimiter!.AttemptAcquire((key, data.Strategy, data));
                    if (!lease.IsAcquired)
                    {
                        long retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan ra)
                            ? (long)Math.Ceiling(ra.TotalSeconds)   
                            : 100;                   
                        response = new RateLimitResponse(false, 0, retryAfter);
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
            catch (Exception e)
            {
                _logger.LogError($"Exception:{e.Message} at memory rate limiting");
                return;
            }
        }
        await _next(context);
    }
}