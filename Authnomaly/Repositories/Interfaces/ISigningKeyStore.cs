using Authnomaly.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Repositories.Interfaces;


public interface ISigningKeyStore
{
    Task<List<SigningKey>> GetCurrentActiveKeysAsync();
    Task<bool> RotateKeyValuePair(RsaSecurityKey publicKey,RsaSecurityKey privateKey);
    Task<SigningKey> GetLastActivePair();
    Task CleanupExpiredKeys();
}