using System.Collections.Concurrent;
using System.Net;
using Authnomaly.Controllers;
using Authnomaly.Utils;
using Microsoft.AspNetCore.Http.Extensions;

namespace Authnomaly.Middlewares.RateLimiting;

public class EndpointFilter
{
    // {.../login:IP:TimeSpan:tries,/login:USERNAME:TimeSpan:tries}
    private readonly RequestDelegate _next;
    private readonly IDictionary<String, KeyValuePair<TimeSpan, int>> apiData;
    private IRateLimitter _limiter;
    public EndpointFilter(RequestDelegate next,IRateLimitter limiter)
    {
        _next = next;
        _limiter = limiter;
        apiData = new ConcurrentDictionary<string, KeyValuePair<TimeSpan, int>>();
        apiData.Add("rl:login:IP",new KeyValuePair<TimeSpan, int>(TimeSpan.FromMinutes(1),100));
        apiData.Add("rl:login:USERNAME",new KeyValuePair<TimeSpan, int>(TimeSpan.FromMinutes(1),20));
    }
    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        var body = context.Request.Body;
        var url = context.Request.GetDisplayUrl();
        var key = RateLimitUtils.GetMatch(new RateLimitData(context));
        Console.WriteLine(key[0]);
        if (!await _limiter.IncrementKey(key[0],TimeSpan.FromMinutes(1), 100))
        {
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            return;
        }
        await _next(context);
    }
}