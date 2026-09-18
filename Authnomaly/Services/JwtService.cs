using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;

namespace Authnomaly.Services;

public class JwtService
{
    private readonly ISigningKeyStore _securityKeysRepo;
    public JwtService(ISigningKeyStore securityKeysRepo)
    {
        _securityKeysRepo = securityKeysRepo;
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
    public bool InvalidateTokenFamily(string jwtToken)
    {
        return true;
    }
    private bool InvalidateToken(string jwtToken)
    {
        /**/
        return true;
    }
}