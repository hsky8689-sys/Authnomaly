using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class EndpointLimitDataRepository : IRateLimitKeysRepo
{
    private readonly AuthnomalyDatabaseContext _context;
    public EndpointLimitDataRepository(AuthnomalyDatabaseContext context)
    {
        _context = context;
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
        return await _context.EndpointLimitDatas
            .Where(el => el.Id == id)
            .ExecuteDeleteAsync()
            == 1;
    }
    public async Task<long> Add(EndpointLimitData entity)
    {
        try
        {
            return await _context.Database
            .SqlQuery<long>($@"SELECT add_endpoint_ratelimit_strategy(
	                                    {entity.Path},
	                                    {entity.Method.ToString()},
	                                    {entity.FieldName},
                                        {entity.Order},
	                                    {entity.WindowSeconds},
	                                    {entity.Limit}
	        ) AS ""Value""")
            .SingleOrDefaultAsync();
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
        catch (Exception e)
        {
            throw;
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
        return await _context.EndpointLimitDatas
            .Where(el => el.Id == id)
            .ExecuteUpdateAsync(
                setters=>setters
                    .SetProperty(el=>el.Limit,el=>entity.Limit)
                    .SetProperty(el=>el.WindowSeconds,el=>entity.WindowSeconds)
                    .SetProperty(el=>el.Order,el=>entity.Order)
                    .SetProperty(el=>el.Method,el=>entity.Method)
                    .SetProperty(el=>el.FieldName,el=>entity.FieldName)
                    .SetProperty(el=>el.Source,el=>entity.Source)
            ) 
               == 1;
    }
}