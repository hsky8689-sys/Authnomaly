namespace Authnomaly.Middlewares.RateLimiting;

public interface IRateLimitter
{
    Task<RateLimitResponse> IncrementKey(string key, TimeSpan ttl, int limit);
}