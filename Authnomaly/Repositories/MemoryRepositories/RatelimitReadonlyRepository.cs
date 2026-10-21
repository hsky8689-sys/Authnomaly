using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using StackExchange.Redis;

namespace Authnomaly.Repositories.MemoryRepositories;

public class RatelimitReadonlyRepository:IReadOnlyLimitKeysRepo
{
    private readonly IDatabase db;
    public RatelimitReadonlyRepository(IConnectionMultiplexer multiplexer)
    {
        db = multiplexer.GetDatabase();
    } 
    public async Task<IList<EndpointLimitData>> FindRulesOrdered(HttpMethod method, string path)
    {
        RedisValue[] ids = await db.SortedSetRangeByScoreAsync($"rl:rules:{method.Method}:{path}");
        if (ids.Length == 0) return new List<EndpointLimitData>();
        var batch = db.CreateBatch();
        var tasks = ids.Select(id => batch.HashGetAllAsync($"rl:rule:{id}")).ToArray();
        batch.Execute();
        var hashes = await Task.WhenAll(tasks);
        return ids.Zip(hashes).Where(p => p.Second.Length > 0)
            .Select(p => FromHash((long)p.First,p.Second))
            .ToList();
    }
    public async Task<EndpointLimitData> FindById(long id)
    {
        HashEntry[] hashEntry = await db.HashGetAllAsync($"rl:rule:{id}");
        return hashEntry.Length == 0 ? new EndpointLimitData(0) : FromHash(id, hashEntry);
    }
    private static HashEntry[] ToHash(EndpointLimitData r) =>
    [
        new("method", r.Method.Method), new("path", r.Path), new("order", r.Order),
        new("source", (int)r.Source), new("field", r.FieldName),
        new("window", r.WindowSeconds), new("limit", r.Limit), new("strategy", (int)r.Strategy)
    ];
    private static EndpointLimitData FromHash(long id, HashEntry[] h)
    {
        var d = h.ToDictionary(); // extensie din StackExchange.Redis
        return new EndpointLimitData(id,
            HttpMethod.Parse((string)d["method"]!), (string)d["path"]!, (int)d["order"],
            (RateLimitSource)(int)d["source"], (int)d["window"], (int)d["limit"],
            (string?)d["field"], (RateLimitStrategy)(int)d["strategy"]);
    }
}