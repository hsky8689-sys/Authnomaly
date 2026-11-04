using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
         * Creates the key that maps to the saved
         * rules for a strategy having a certain id
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
    private static string ClientIpValue(HttpContext context)
    {
        /*
         * ipv4 sent like ::ffff:1.2.3.4 uses the same counter as 1.2.3.4;
         * real ipv6 values remain unchanged
         */
        var ip = context.Connection.RemoteIpAddress;
        if (ip is not null && ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return $"ip:{ip}";
    }
    private static async Task<string> BodyAttributeValue(EndpointLimitData data,HttpContext context)
    {
        /*
         * Citeste campul din body fara sa il consume pentru controller;
         * body gol, json invalid sau camp lipsa -> contor pe ip,
         * ca un request fara camp sa nu poata ocoli regula
         */
        var body = context.Request.Body;
        body.Position = 0;
        string json;
        using (var reader=new StreamReader(body,Encoding.UTF8,leaveOpen:true))
        {
            json=await reader.ReadToEndAsync();
        }
        body.Position = 0;
        if (json.Length==0) return ClientIpValue(context);//if no such header is present,we return an ip-based rate-limit key
        try
        {
            var content = JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(json);
            var field = content?.FirstOrDefault(kv => string.Equals(kv.Key,data.FieldName,StringComparison.OrdinalIgnoreCase));
            if (field?.Key is null) return ClientIpValue(context);
            string raw = field.Value.Value.ValueKind == JsonValueKind.String ? field.Value.Value.GetString()!
                                                                             : field.Value.Value.GetRawText();
            return $"body:{raw.Trim().ToLowerInvariant()}";
        }
        catch (JsonException)
        {
            return ClientIpValue(context);
        }
    }
    public static async Task<string> CreateCounterCacheKeyAfterEntity(EndpointLimitData data,HttpContext context)
    {
        /*
         * Creates the key after which the counter
         * of a real-time monitored rate limit instance
         * after parameters based of parameters from header/body/ip adress
         * valoarea e hash-uita ca atacatorul sa nu controleze lungimea cheii
         */
        string value = data.Source switch
        {
            RateLimitSource.BodyField=>await BodyAttributeValue(data,context),
            _=>ClientIpValue(context)
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
        return $"rl:cnt:{data.Id}:{hash}";
    }
}