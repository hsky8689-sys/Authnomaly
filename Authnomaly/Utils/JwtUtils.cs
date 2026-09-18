using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Utils;

public class JwtUtils
{
    public static KeyValuePair<RsaSecurityKey,RsaSecurityKey> CreatePair()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true))
        {
            KeyId = Guid.NewGuid().ToString() 
        };
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false))
        {
            KeyId = privateKey.KeyId
        };
        return new KeyValuePair<RsaSecurityKey, RsaSecurityKey>(publicKey, privateKey);
    }

    public static string ExportPrivateKeyPem(RsaSecurityKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(key.Parameters);
        return rsa.ExportRSAPrivateKeyPem();
    }

    public static string ExportPublicKeyPem(RsaSecurityKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(key.Parameters);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }
    public static string CreateJwt(string username,SecurityKey privateKey)
    {
        var handler = new JsonWebTokenHandler();
        var subject = new ClaimsIdentity(
            new[]
            {
                new Claim("username",username),
                new Claim("roles","user"),
                new Claim("trust","100")//changed after anomaly detection features start
            }
            );
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Issuer = "http://localhost:5000",
            Expires = DateTime.UtcNow.AddMinutes(30),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(privateKey,SecurityAlgorithms.RsaSha256)
        };
        return handler.CreateToken(tokenDescriptor);
    }
    public async static Task<bool> ValidateJwt(string jwt,RsaSecurityKey publicKey)
    {
        var handler = new JsonWebTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "http://localhost:5000",
            
            ValidateAudience = true,
            ValidAudience = "http://localhost:5000",
            
            ValidateLifetime = true,
            
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = publicKey,
            
            ValidAlgorithms = new [] {SecurityAlgorithms.RsaSha256},
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        TokenValidationResult resutl = await handler.ValidateTokenAsync(jwt, validationParameters);
        return resutl.IsValid;
    }
}