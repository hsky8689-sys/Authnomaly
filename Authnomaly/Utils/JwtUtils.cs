using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Utils;

public class JwtUtils
{
    private static string jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? throw new ArgumentException("NO JWT ISSUER SET UP");
    private static string jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? throw new ArgumentException("NO JWT AUDIENCE SET UP");
    private static int jwtExpirySeconds = Int32.Parse(Environment.GetEnvironmentVariable("JWT_EXPIRE") ?? throw new ArgumentException("NO JWT EXPIRE TIME SET UP"));
    private static int jwtClockSkew = Int32.Parse(Environment.GetEnvironmentVariable("JWT_CLOCK_SKEW")??"5");//default skew value of 5 seconds 
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
    public static string CreateJwt(string username,Guid familyId,SecurityKey privateKey)
    {
        var handler = new JsonWebTokenHandler();
        var subject = new ClaimsIdentity(
            new[]
            {
                new Claim("username",username),
                new Claim("roles","user"),
                new Claim("jti",Guid.NewGuid().ToString()),
                new Claim("familyId",familyId.ToString())
            }
            );
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Issuer = jwtIssuer,
            Audience = jwtAudience,
            Expires = DateTime.UtcNow.AddSeconds(jwtExpirySeconds),
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
            ValidIssuer = jwtIssuer,
            
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            
            ValidateLifetime = true,
            
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = publicKey,
            
            ValidAlgorithms = new [] {SecurityAlgorithms.RsaSha256},
            ClockSkew = TimeSpan.FromSeconds(jwtClockSkew)
        };
        TokenValidationResult result = await handler.ValidateTokenAsync(jwt, validationParameters);
        return result.IsValid;
    }
}