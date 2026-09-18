using Authnomaly.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Services;

public interface ISigningKeyProtection
{
    string Encrypt(string privateKeyPem);
    string Decrypt(string encrypted);
    RsaSecurityKey GetPrivateKey(SigningKey key);
}