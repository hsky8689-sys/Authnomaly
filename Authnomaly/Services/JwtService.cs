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
        return await CreateFirstSigningKey();
    }
    public async Task<List<SigningKey>> GetActiveSigningKeys()
    {
        var active = await _securityKeysRepo.GetCurrentActiveKeysAsync();
        if (active.Count == 0)
        {
            await CreateFirstSigningKey();
            return await _securityKeysRepo.GetCurrentActiveKeysAsync();
        }
        return active;
    }
    // No current key: only insert, never deactivate. If a concurrent caller inserted first,
    // the insert fails on the unique index and we read the key it created.
    private async Task<SigningKey> CreateFirstSigningKey()
    {
        var pair = JwtUtils.CreatePair();
        await _securityKeysRepo.AddKeyValuePair(pair.Key, pair.Value);
        return await _securityKeysRepo.GetLastActivePair();
    }
    // Deactivate + insert run in one transaction. If a concurrent call wins the partial unique index,
    // our insert fails, the transaction is rolled back (old key stays as the other call left it)
    // and we return the key that is current now.
    public async Task<SigningKey> RotateSigningKey()
    {
        var pair = JwtUtils.CreatePair();
        await using (var transaction = await _securityKeysRepo.BeginTransactionAsync())
        {
            if (await _securityKeysRepo.RotateKeyValuePair(pair.Key, pair.Value))
            {
                await transaction.CommitAsync();
            }
            // not committed -> disposing the transaction rolls it back
        }
        return await _securityKeysRepo.GetLastActivePair();
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

    public async Task<(double? latitude, double? longitude,DateTimeOffset timestamp)> GetLastLocation(string endpoint, string username)
    {
        if(endpoint.Length == 0) return default;
        if (username.Length == 0) return default;
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