using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class SigningKeysRepository : ISigningKeyStore
{
    private readonly AuthnomalyDatabaseContext _context;
    private readonly ISigningKeyProtection _protection;
    public SigningKeysRepository(AuthnomalyDatabaseContext context, ISigningKeyProtection protection)
    {
        _context = context;
        _protection = protection;
    }
    public async Task<List<SigningKey>> GetCurrentActiveKeysAsync()
    {
        return await _context.SigningKeys.AsNoTracking()
                                         .Where(k => k.IsCurrent)
                                         .ToListAsync();
    }
    public async Task<bool> RotateKeyValuePair(RsaSecurityKey publicKey, RsaSecurityKey privateKey)
    {
        var contextSet = _context.SigningKeys;
        await contextSet.Where(k => k.IsCurrent)
                         .ExecuteUpdateAsync(
                             setters=>setters.SetProperty(k=>k.IsCurrent,k=>false)
                                            );
        var publicPem = JwtUtils.ExportPublicKeyPem(publicKey);
        var privatePem = JwtUtils.ExportPrivateKeyPem(privateKey);
        var encryptedPrivate = _protection.Encrypt(privatePem);
        SigningKey key = new SigningKey(0, publicKey.KeyId, publicPem, encryptedPrivate);
        await contextSet.AddAsync(key);
        return (await _context.SaveChangesAsync()) == 1;
    }
    public async Task<SigningKey> GetLastActivePair()
    {
        var found = await _context.SigningKeys.Where(k => k.IsCurrent)
                                                           .OrderBy(k => k.CreatedAt)
                                                           .FirstOrDefaultAsync();
        return found is not null ? found : new SigningKey(0);
    }
    public async Task CleanupExpiredKeys()
    {
        await _context.SigningKeys
            .Where(k => k.RetiredAt.HasValue && DateTimeOffset.Compare(k.RetiredAt.Value, DateTimeOffset.UtcNow) < 0)
            .Select(u=>u)
            .ExecuteUpdateAsync<SigningKey>(setters=>setters.SetProperty(k=>k.IsCurrent,k=>false));
    }
}