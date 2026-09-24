using Authnomaly.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Authnomaly.Repositories.MemoryRepositories;

public class TokenBlacklistRepository : ITokenBlacklistStore
{
    private readonly IDistributedCache _cache;
    public TokenBlacklistRepository(IDistributedCache cache)
    {
        _cache = cache;
    }
    public async Task<bool> AddToBlacklist(string jti,
                                           int ttl = -1,
                                           BlacklistLevel reason = 0)
    {
        try
        {
            if (reason == BlacklistLevel.NotBlacklisted) throw new ArgumentException("Reasons 1-4 are valid for this type of operation");
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttl)
            };
            await _cache.SetStringAsync(
                $"blacklist:{jti}", 
                reason.ToString(),
                options
            );
            return true;
        }
        catch
        {
            Console.WriteLine("Null argument passed");
            return false;
        }
    }
    public async Task<BlacklistLevel> IsBlacklisted(string jti)
    {
        var obtained = await _cache.GetStringAsync($"blacklist:{jti}");
        if (obtained is null) return BlacklistLevel.NotBlacklisted;
        var reason = BlacklistLevel.NotBlacklisted;
        if (Enum.TryParse<BlacklistLevel>(obtained, out reason))
        {
            if (Enum.IsDefined(typeof(BlacklistLevel), reason)) return reason;
            else Console.WriteLine("wrong value saved in redis");
        }
        return reason;
    }
    public async Task<string?> GetCurrentFamilyJti(Guid familyId)
    {
        var result = await _cache.GetStringAsync($"refreshToken:{familyId}");
        return result;
    }
    public async Task SetCurrentFamilyJti(Guid familyId, string jti, TimeSpan ttl)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };
        await _cache.SetStringAsync(
            $"refreshToken:{familyId.ToString()}", 
            jti,
            options
        );
    }
    public async Task RevokeFamily(Guid familyId)
    {
        await _cache.RemoveAsync($"refreshToken:{familyId.ToString()}");
    }
    public async Task<bool> IsFamilyRevoked(Guid familyId)
    {
        return await _cache.GetAsync($"refreshToken:{familyId.ToString()}") == null;
    }
    public async Task<(double? latitude, double? longitude,DateTimeOffset timestamp)> GetLastLocation(string endpoint,string username)
    {
       /*salvam latitudinea si longitudinea ultimelei conectari reusite
        la endpointuri protejate(non login/register) la fiecare user*/
       var saved = await _cache.GetStringAsync($"last:{endpoint}:{username}");
       if (saved is null) return (null, null,DateTimeOffset.UtcNow);
       var latLon = saved.Split("/");
       return (Double.Parse(latLon[0]), Double.Parse(latLon[1]),DateTimeOffset.Parse(latLon[2]));
    }
}