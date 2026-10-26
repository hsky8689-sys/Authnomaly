using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class EndpointLimitDataRepository : IRateLimitKeysRepo
{
    private readonly AuthnomalyDatabaseContext _context;
    private readonly IDatabase _db;
    public EndpointLimitDataRepository(AuthnomalyDatabaseContext context,
                                        IConnectionMultiplexer multiplexer)
    {
        _context = context;
        _db = multiplexer.GetDatabase();
    } 
    public async Task<EndpointLimitData> FindById(long id)
    {
        var found =  await _context.EndpointLimitDatas
                   .Where(el => el.Id == id)
                   .SingleOrDefaultAsync();
        return found is not null ? found : new EndpointLimitData(0);
    }
    public async Task<bool> Delete(long id)
    {
        try
        {
            var query = _context.EndpointLimitDatas.Where(el => el.Id == id);
            var found = await query.FirstOrDefaultAsync();
            if (found is null) return false;
            bool deleted = await query.ExecuteDeleteAsync() == 1;
            if (deleted)
            {
                string method = found!.Method.Method;
                string path = found!.Path;
                await _db.KeyDeleteAsync(RatelimitUtils.CreateRuleCacheKey(id));                                 
                await _db.SortedSetRemoveAsync(RatelimitUtils.CreateCacheKey(method,path), id);
            }
            return deleted;
        }
        catch (SystemException e) when (e.InnerException is ArgumentNullException | 
                                        e.InnerException is InvalidOperationException |
                                        e.InnerException is OperationCanceledException)
        {
            return false;
        }
    }
    public async Task<long> Add(EndpointLimitData entity)
    {
        try
        {
            long added =  await _context.Database
            .SqlQuery<long>($@"SELECT add_endpoint_ratelimit_strategy(
	                                    {entity.Path},
	                                    {entity.Method.ToString()},
	                                    {entity.FieldName},
                                        {entity.Order},
	                                    {entity.WindowSeconds},
	                                    {entity.Limit},
                                        {entity.Strategy}
	        ) AS ""Value""")
            .SingleOrDefaultAsync();
            if (added > 0)
            {
                var firstHash = RatelimitUtils.ToHash(entity);
                var data = RatelimitUtils.FromHash(added, firstHash);
                var key = RatelimitUtils.CreateCacheKey(data.Method.Method,data.Path);
                //await _db.HashSetAsync(key, RatelimitUtils.ToHash(data));
                await _db.HashSetAsync(RatelimitUtils.CreateRuleCacheKey(added), RatelimitUtils.ToHash(data));
                await _db.SortedSetAddAsync(key, added, data.Order);
                var rk = new RedisKey(key);
                if (await _db.KeyTimeToLiveAsync(rk) != null)
                {
                    return await _db.KeyPersistAsync(rk) ? added : 0;
                }
            }
            return added;
        }
        catch (PostgresException e) when (e.InnerException is PostgresException
                                          {
                                              SqlState: PostgresErrorCodes.UniqueViolation
                                          })
        {
            _context.Entry(entity).State = EntityState.Detached;
            return 0;
        }
        catch (SystemException e) when (e.InnerException is ArgumentNullException | 
                                        e.InnerException is InvalidOperationException |
                                        e.InnerException is OperationCanceledException)
        {
            return 0;
        }
    }
    public async Task<IList<EndpointLimitData>> FindRulesOrdered(HttpMethod method, string path)
    {
        return await _context.EndpointLimitDatas
            .Where(el => el.Method.Equals(method) && el.Path.Equals(path))
            .OrderBy(el => el.Order)
            .ToListAsync();
    }
    public async Task<bool> EditRules(long id, EndpointLimitData entity)
    {
        int updated =  await _context.EndpointLimitDatas
            .Where(el => el.Id == id)
            .ExecuteUpdateAsync(
                setters=>setters
                    .SetProperty(el=>el.Limit,el=>entity.Limit)
                    .SetProperty(el=>el.WindowSeconds,el=>entity.WindowSeconds)
                    .SetProperty(el=>el.Order,el=>entity.Order)
                    .SetProperty(el=>el.Method,el=>entity.Method)
                    .SetProperty(el=>el.FieldName,el=>entity.FieldName)
                    .SetProperty(el=>el.Source,el=>entity.Source)
            );
        if (updated > 0)
        {
            string rediskey = RatelimitUtils.CreateRuleCacheKey(id);
            HashEntry[] newStats = RatelimitUtils.ToHash(entity);
            await _db.HashSetAsync(rediskey,newStats);
        }
        return updated > 0;
    }
}