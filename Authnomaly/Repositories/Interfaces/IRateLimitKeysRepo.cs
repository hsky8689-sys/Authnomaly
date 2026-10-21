using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface IRateLimitKeysRepo : IRepo<EndpointLimitData,long>
{
    Task<IList<EndpointLimitData>> FindRulesOrdered(HttpMethod method,string path);
}
public interface IReadOnlyLimitKeysRepo
{
    Task<IList<EndpointLimitData>> FindRulesOrdered(HttpMethod method,string path);
    Task<EndpointLimitData> FindById(long id);
}