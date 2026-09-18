using System.Security.Cryptography;
using Authnomaly.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Services;

public class DataProtectionAPIService : ISigningKeyProtection
{
    private readonly IDataProtector _protector;
    public DataProtectionAPIService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Authnomaly.SigningKeys");
    }
    public string Encrypt(string privateKeyPem) => _protector.Protect(privateKeyPem);
    public string Decrypt(string encrypted) => _protector.Unprotect(encrypted);
    public RsaSecurityKey GetPrivateKey(SigningKey key)
    {
        var decryptedPrivatePem = _protector.Unprotect(key.EncryptedPrivateKey);
        var rsaPrivate = RSA.Create();
        rsaPrivate.ImportFromPem(decryptedPrivatePem);
        return new RsaSecurityKey(rsaPrivate) { KeyId = key.Kid };
    }
}