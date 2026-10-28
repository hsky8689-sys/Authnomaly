using System.Threading.RateLimiting;
using Authnomaly.Domain;
using Authnomaly.Middlewares.RateLimiting;
using StackExchange.Redis;

namespace Authnomaly.Utils;

public class RatelimitUtils
{
    private static readonly int _defaultSlidingWindowLimit = Int32.Parse(Environment.GetEnvironmentVariable("DEFAULT_SLIDING_WINDOW_LIMIT")!);
    private static readonly int _defaultSlidingWindow_Window =  Int32.Parse(Environment.GetEnvironmentVariable("DEFAULT_SLIDING_WINDOW_WINDOW")!);
    private static readonly int _defaultFixedWindowLimit = Int32.Parse(Environment.GetEnvironmentVariable("DEFAULT_FIXED_WINDOW_LIMIT")!);
    private static readonly int _defaultFixedWindow_Window = Int32.Parse(Environment.GetEnvironmentVariable("DEFAULT_FIXED_WINDOW_WINDOW")!);
    private static readonly int _defaultBucketLimit = Int32.Parse(Environment.GetEnvironmentVariable("DEFAULT_BUCKET_LIMIT")!);
    private static readonly int _defaultBucket_Window = Int32.Parse(Environment.GetEnvironmentVariable("DEFAULT_BUCKET_WINDOW")!);
    private static readonly string _dbRateLimitKey = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_RATELIMIT_KEY")!;
    public static HashEntry[] ToHash(EndpointLimitData r) =>
    [
        new("method", r.Method.Method), new("path", r.Path), new("order", r.Order),
        new("source", (int)r.Source), new("field", r.FieldName),
        new("window", r.WindowSeconds), new("limit", r.Limit), new("strategy", (int)r.Strategy)
    ];
    public static EndpointLimitData FromHash(long id, HashEntry[] h)
    {
        var d = h.ToDictionary(); // extensie din StackExchange.Redis
        return new EndpointLimitData(id,
            HttpMethod.Parse((string)d["method"]!), (string)d["path"]!, (int)d["order"],
            (RateLimitSource)(int)d["source"], (int)d["window"], (int)d["limit"],
            (string?)d["field"], (RateLimitStrategy)(int)d["strategy"]);
    }
    public static string CreateCacheKey(string method,string path)
    {
        /*
         * Creeaza cheia dupa care sunt cautate
         * si sortate dupa ordine
         * regulile aferente
         * apelurilor de metoda catre un anumit path
         */
        return $"rl:rules:{method}:{path}";
    }
    public static string CreateRuleCacheKey(long id)
    {
        /*
         * Creeaza cheia dupa care este cautat set-ul
         * de valori aferente strategiei cu un anume id
         */
        return $"rl:rule:{id}";
    }
    public static IRateLimitter MakeRateLimiter(RateLimitStrategy strategy,IConnectionMultiplexer multiplexer)
    {
        /*
         * Parameters:strategy-defines the Rate Limitter strategy
         * Multiplexer:needed to establish in memory database connection
         * Returns:a custom rate limiter instance
         */
        return strategy switch
        {
            RateLimitStrategy.Bucket => new BucketRateLimitter(multiplexer),
            RateLimitStrategy.FixedWindow => new FixedWindowRateLimitter(multiplexer),
            RateLimitStrategy.SlidingWindowLog => new SlidingWindowLogRateLimitter(multiplexer),
            RateLimitStrategy.SlidingWindowCounter => new SlidingWindowCounterRateLimiter(multiplexer),
            RateLimitStrategy.LeakyBucket => new LeakyBucketRateLimiter(multiplexer),
            _ => new SlidingWindowLogRateLimitter(multiplexer) // default fallback
        };
    }
    public static RateLimitPartition<string> MakeFallbackRateLimiter(RateLimitStrategy strategy,string key,EndpointLimitData? collectedData=null)
    {
        /*
         * Parameters:strategy-defines the Rate Limitter strategy
         * key:the limitted endpoint key
         * collectedData:endpoint data collected from the database or null if it could not be collected
         * Returns:the partition (limiter recipe) used by a PartitionedRateLimiter for this key
         */
        if (key.Equals(_dbRateLimitKey))
        {
            return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                new SlidingWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = collectedData?.Limit ?? _defaultSlidingWindowLimit ,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    SegmentsPerWindow = 2,
                    Window = TimeSpan.FromSeconds(collectedData?.WindowSeconds ?? _defaultSlidingWindow_Window)
                });
        }
        if (key.Equals(_dbRateLimitKey) && collectedData is null)
        {
            throw new ArgumentException();
        }
        return strategy switch
        {
            RateLimitStrategy.FixedWindow=>RateLimitPartition.GetFixedWindowLimiter(key, _ =>
                new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment  = true,
                    PermitLimit = collectedData?.Limit ?? _defaultFixedWindowLimit,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    Window = TimeSpan.FromSeconds(collectedData?.WindowSeconds ?? _defaultFixedWindow_Window)
                }),
            RateLimitStrategy.SlidingWindowCounter=>RateLimitPartition.GetSlidingWindowLimiter(key,_=>
                new SlidingWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = collectedData?.Limit ?? _defaultSlidingWindowLimit,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    SegmentsPerWindow = 2,
                    Window = TimeSpan.FromSeconds(collectedData?.WindowSeconds ?? _defaultSlidingWindow_Window)
                }),
            RateLimitStrategy.SlidingWindowLog=>RateLimitPartition.GetSlidingWindowLimiter(key,_=>
                new SlidingWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = collectedData?.Limit ?? _defaultSlidingWindowLimit,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    SegmentsPerWindow = 2,
                    Window = TimeSpan.FromSeconds(collectedData?.WindowSeconds ?? _defaultSlidingWindow_Window)
                }),
            RateLimitStrategy.Bucket=>RateLimitPartition.GetTokenBucketLimiter(key,_=>
                new TokenBucketRateLimiterOptions
                {
                    AutoReplenishment = true,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(collectedData?.WindowSeconds ?? _defaultBucket_Window),
                    TokenLimit = collectedData?.Limit ?? _defaultBucketLimit,
                    TokensPerPeriod = collectedData?.Limit ?? _defaultBucketLimit
                }),
            RateLimitStrategy.LeakyBucket=>RateLimitPartition.GetNoLimiter(key)
        };
    }
    public static string CreateCacheKeyAfterEntity(EndpointLimitData entity)
    {
        /*
         * Returneaza cheia dupa care este este stocata setul
         * de valori aferente dupa diferiti parametrii bazati pe
         * sursa lor (header/body etc)
         */
        throw new NotImplementedException();
    }
}