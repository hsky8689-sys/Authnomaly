using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface IRateLimitKeysRepo : IRepo<EndpointLimitData,long>
{
    Task<IList<EndpointLimitData>> FindRulesOrdered(HttpMethod method,string path);
}