using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;
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
            .Select(p => RatelimitUtils.FromHash((long)p.First,p.Second))
            .ToList();
    }
    public async Task<EndpointLimitData> FindById(long id)
    {
        HashEntry[] hashEntry = await db.HashGetAllAsync($"rl:rule:{id}");
        return hashEntry.Length == 0 ? new EndpointLimitData(0) : RatelimitUtils.FromHash(id, hashEntry);
    }
}