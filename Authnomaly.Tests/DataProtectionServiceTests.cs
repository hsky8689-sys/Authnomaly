using System.Security.Cryptography;
using Authnomaly.Domain;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.DataProtection;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public class DataProtectionServiceTests
{
    private readonly IDataProtector _protector;
    private readonly ITestOutputHelper _output;
    private readonly ISigningKeyProtection _protectionService;
    public DataProtectionServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _protector = DataProtectionProvider.Create("test-app").CreateProtector("Authnomaly.SigningKeys");
        _protectionService = new DataProtectionAPIService(_protector);
    }

    private static string NewPrivatePem() => JwtUtils.ExportPrivateKeyPem(JwtUtils.CreatePair().Value);

    // a service with its own throwaway key ring: ciphertext from any other ring must not decrypt here
    private static DataProtectionAPIService NewIsolatedService() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void EncryptThenDecrypt_ReturnsTheSamePem()
    {
        var pem = NewPrivatePem();

        Assert.Equal(pem, _protectionService.Decrypt(_protectionService.Encrypt(pem)));
    }

    [Fact]
    public void Encrypt_DoesNotLeakThePem_AndIsRandomized()
    {
        var pem = NewPrivatePem();

        var first = _protectionService.Encrypt(pem);
        var second = _protectionService.Encrypt(pem);

        Assert.NotEqual(pem, first);
        Assert.DoesNotContain("PRIVATE KEY", first);
        Assert.NotEqual(first, second); // same input, different ciphertext: never compare ciphertexts for equality
        Assert.Equal(pem, _protectionService.Decrypt(second));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var encrypted = _protectionService.Encrypt(NewPrivatePem());
        var tampered = encrypted[..^4] + (encrypted.EndsWith("AAAA") ? "BBBB" : "AAAA");

        Assert.ThrowsAny<CryptographicException>(() => _protectionService.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_GarbageString_Throws()
    {
        Assert.ThrowsAny<CryptographicException>(() => _protectionService.Decrypt("not-a-ciphertext"));
    }

    [Fact]
    public void Decrypt_CiphertextFromAnotherKeyRing_Throws()
    {
        var encryptedElsewhere = NewIsolatedService().Encrypt(NewPrivatePem());

        Assert.ThrowsAny<CryptographicException>(() => _protectionService.Decrypt(encryptedElsewhere));
    }

    [Fact]
    public void Decrypt_CiphertextProtectedForAnotherPurpose_Throws()
    {
        var foreign = DataProtectionProvider.Create("test-app").CreateProtector("Other.Purpose").Protect(NewPrivatePem());

        Assert.ThrowsAny<CryptographicException>(() => _protectionService.Decrypt(foreign));
    }

    [Fact]
    public async Task GetPrivateKey_ReturnsAKeyThatSignsTokensValidWithTheOriginalPublicKey()
    {
        var pair = JwtUtils.CreatePair();
        var stored = new SigningKey(0, pair.Key.KeyId,
            JwtUtils.ExportPublicKeyPem(pair.Key),
            _protectionService.Encrypt(JwtUtils.ExportPrivateKeyPem(pair.Value)));

        var restored = _protectionService.GetPrivateKey(stored);
        var jwt = JwtUtils.CreateJwt("testuser", Guid.NewGuid(), restored);

        Assert.Equal(pair.Key.KeyId, restored.KeyId);
        Assert.True(await JwtUtils.ValidateJwt(jwt, pair.Key));
    }

    [Fact]
    public async Task GetPrivateKey_TokenIsRejectedByAnUnrelatedPublicKey()
    {
        var pair = JwtUtils.CreatePair();
        var unrelated = JwtUtils.CreatePair();
        var stored = new SigningKey(0, pair.Key.KeyId,
            JwtUtils.ExportPublicKeyPem(pair.Key),
            _protectionService.Encrypt(JwtUtils.ExportPrivateKeyPem(pair.Value)));

        var jwt = JwtUtils.CreateJwt("testuser", Guid.NewGuid(), _protectionService.GetPrivateKey(stored));

        Assert.False(await JwtUtils.ValidateJwt(jwt, unrelated.Key));
    }

    [Fact]
    public void GetPrivateKey_GarbageEncryptedValue_Throws()
    {
        var stored = new SigningKey(0, "kid", "public-pem", "garbage");

        Assert.ThrowsAny<CryptographicException>(() => _protectionService.GetPrivateKey(stored));
    }

    [Fact]
    public async Task ConcurrentRoundTrips_OnTheSameService_AllSucceed()
    {
        var pem = NewPrivatePem();

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            var input = pem + i;
            return _protectionService.Decrypt(_protectionService.Encrypt(input)) == input;
        })));

        Assert.All(results, Assert.True);
    }
}