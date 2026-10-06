namespace Authnomaly.Middlewares.RateLimiting;

public interface IRateLimitter
{
    Task<bool> IncrementKey(string key, TimeSpan ttl, int limit);
}