using System.Net;
using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.AspNetCore.Http.Extensions;
using StackExchange.Redis;

namespace Authnomaly.Middlewares.RateLimiting;
public class LimiterFactory
{
    private readonly IConnectionMultiplexer _multiplexer; 
    public LimiterFactory(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }
    public IRateLimitter MakeLimiter(RateLimitStrategy strategy)
    {
        return strategy switch
        {
            RateLimitStrategy.Bucket => null,
            RateLimitStrategy.FixedWindow => new FixedWindowRateLimitter(_multiplexer),
            RateLimitStrategy.SlidingWindow => null
        };
    } 
}
public class EndpointFilter
{
    private readonly RequestDelegate _next;
    private IRateLimitter _limiter;
    private static LimiterFactory _factory;
    private static IRateLimitKeysRepo _limitDataRepository;
    public EndpointFilter(RequestDelegate next,
                          IConnectionMultiplexer multiplexer,
                          IRateLimitKeysRepo limitDataRepo)
    {
        _next = next;
        _factory = new LimiterFactory(multiplexer);
        _limitDataRepository = limitDataRepo;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        var method = HttpMethod.Parse(context.Request.Method);
        var url = context.Request.GetDisplayUrl();
        IList<EndpointLimitData> found = await _limitDataRepository.FindRulesOrdered(method, url);
        foreach (EndpointLimitData data in found)
        { 
            _limiter = _factory.MakeLimiter(data.Strategy);
            string key = $"rl:{data.Id}:{() => {
                if(data.Source.Equals(RateLimitSource.Ip))
                    return context.Connection.RemoteIpAddress;
                //extragem din body atributul cerut
            } }";
            if (!await _limiter.IncrementKey(key,TimeSpan.FromSeconds(data.WindowSeconds),data.Limit))
            {
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                return;
            }
        }
        await _next(context);
    }
}