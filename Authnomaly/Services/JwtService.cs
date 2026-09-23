using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;

namespace Authnomaly.Services;

public class JwtService
{
    private readonly ISigningKeyStore _securityKeysRepo;
    private readonly ITokenBlacklistStore _tokenKeyStoreRepo;
    public JwtService(ISigningKeyStore securityKeysRepo,
                      ITokenBlacklistStore tokenKeyStoreRepo)
    {
        _securityKeysRepo = securityKeysRepo;
        _tokenKeyStoreRepo = tokenKeyStoreRepo;
    }

    public async Task<SigningKey> GetLastPrivateKey()
    {
        var last = await _securityKeysRepo.GetLastActivePair();
        if (last.Id != 0) return last;
        var pair = JwtUtils.CreatePair();
        return 
            await _securityKeysRepo.RotateKeyValuePair(pair.Key,pair.Value) 
            ? 
            await _securityKeysRepo.GetLastActivePair() 
            : new SigningKey(0);
    }
    public async Task<List<SigningKey>> GetActiveSigningKeys()
    {
        var active = await _securityKeysRepo.GetCurrentActiveKeysAsync();
        if (active.Count == 0)
        {
            var activeToken = JwtUtils.CreatePair();
            if (await _securityKeysRepo.RotateKeyValuePair(activeToken.Key, activeToken.Value))
            {
                return await _securityKeysRepo.GetCurrentActiveKeysAsync();
            }
            return new List<SigningKey>();
        }
        return active;
    }
    public async Task SetCurrentFamilyJti(Guid familyId, string jti, TimeSpan ttl)
    {
        await _tokenKeyStoreRepo.SetCurrentFamilyJti(familyId, jti, ttl);
    }
    public async Task<string?> GetCurrentFamilyOfJti(string jti)
    {
        var guId = Guid.Parse(jti);
        return await _tokenKeyStoreRepo.GetCurrentFamilyJti(guId);
    }

    public async Task<(double? latitude, double? longitude)> GetLastLocation(string endpoint, string username)
    {
        if(endpoint.Length == 0) return default;
        if (username.Length == 0) return default;
        if (endpoint.Equals("login")) return default;
        if (endpoint.Equals("register")) return default;
        return await _tokenKeyStoreRepo.GetLastLocation(endpoint, username);
    }
    public async Task<bool> InvalidateTokenFamily(string jti,BlacklistLevel reason)
    {
        var currentFamily = await _tokenKeyStoreRepo.GetCurrentFamilyJti(Guid.Parse(jti));
        var guId = Guid.Parse(jti);
        if (currentFamily is null) return false;
        await _tokenKeyStoreRepo.RevokeFamily(guId);
        return (await _tokenKeyStoreRepo.GetCurrentFamilyJti(guId)) == null;
    }
    public async Task<bool> InvalidateToken(string jti,int ttl,BlacklistLevel reason)
    {
        return await _tokenKeyStoreRepo.AddToBlacklist(jti, ttl, reason);
    }
}