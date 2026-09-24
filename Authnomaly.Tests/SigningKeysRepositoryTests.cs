using System.Security.Cryptography;
using System.Text;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Authnomaly.Tests;

// Reversible stand-in for the DataProtection-based implementation, so these tests exercise the repository
// and not the Data Protection key ring
internal sealed class FakeKeyProtection : ISigningKeyProtection
{
    public string Encrypt(string privateKeyPem) => Convert.ToBase64String(Encoding.UTF8.GetBytes(privateKeyPem));
    public string Decrypt(string encrypted) => Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
    public RsaSecurityKey GetPrivateKey(SigningKey key)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(Decrypt(key.EncryptedPrivateKey));
        return new RsaSecurityKey(rsa) { KeyId = key.Kid };
    }
}

[Collection("Database")]
public class SigningKeysRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();

    private SigningKeysRepository NewRepo(TestScope s) => new(s.Context, new FakeKeyProtection());

    private async Task ClearKeys()
    {
        using var s = _db.NewScope();
        await s.Context.SigningKeys.ExecuteDeleteAsync();
    }

    private async Task<string> Rotate()
    {
        var pair = JwtUtils.CreatePair();
        using var s = _db.NewScope();
        Assert.True(await NewRepo(s).RotateKeyValuePair(pair.Key, pair.Value));
        return pair.Key.KeyId;
    }

    [Fact]
    public async Task RotateKeyValuePair_FirstKey_BecomesTheOnlyCurrentKey()
    {
        await ClearKeys();
        try
        {
            var kid = await Rotate();
            using var s = _db.NewScope();
            var current = await NewRepo(s).GetCurrentActiveKeysAsync();
            var single = Assert.Single(current);
            Assert.Equal(kid, single.Kid);
            Assert.True(single.IsCurrent);
            Assert.Null(single.RetiredAt);
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task RotateKeyValuePair_Twice_OnlyTheNewestStaysCurrent()
    {
        await ClearKeys();
        try
        {
            var first = await Rotate();
            var second = await Rotate();
            using var s = _db.NewScope();
            var current = await NewRepo(s).GetCurrentActiveKeysAsync();
            Assert.Equal(second, Assert.Single(current).Kid);
            Assert.Equal(2, await s.Context.SigningKeys.CountAsync());
            Assert.False((await s.Context.SigningKeys.SingleAsync(k => k.Kid == first)).IsCurrent);
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task RotateKeyValuePair_StoresPublicPemAndEncryptedPrivateKey()
    {
        await ClearKeys();
        try
        {
            var pair = JwtUtils.CreatePair();
            using (var s = _db.NewScope()) Assert.True(await NewRepo(s).RotateKeyValuePair(pair.Key, pair.Value));

            using var s2 = _db.NewScope();
            var stored = await NewRepo(s2).GetLastActivePair();
            Assert.Equal(JwtUtils.ExportPublicKeyPem(pair.Key), stored.PublicKey);
            Assert.DoesNotContain("PRIVATE KEY", stored.EncryptedPrivateKey);
            Assert.Equal(JwtUtils.ExportPrivateKeyPem(pair.Value), new FakeKeyProtection().Decrypt(stored.EncryptedPrivateKey));
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task StoredKeyPair_CanSignAndVerifyJwt()
    {
        await ClearKeys();
        try
        {
            var pair = JwtUtils.CreatePair();
            using (var s = _db.NewScope()) await NewRepo(s).RotateKeyValuePair(pair.Key, pair.Value);

            using var s2 = _db.NewScope();
            var stored = await NewRepo(s2).GetLastActivePair();
            var privateKey = new FakeKeyProtection().GetPrivateKey(stored);
            var jwt = JwtUtils.CreateJwt("testuser", Guid.NewGuid(), privateKey);
            Assert.True(await JwtUtils.ValidateJwt(jwt, pair.Key));
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task GetLastActivePair_AfterSeveralRotations_ReturnsTheNewest()
    {
        await ClearKeys();
        try
        {
            await Rotate();
            await Rotate();
            var newest = await Rotate();
            using var s = _db.NewScope();
            Assert.Equal(newest, (await NewRepo(s).GetLastActivePair()).Kid);
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task GetLastActivePair_NoKeys_ReturnsIdZeroSentinel()
    {
        await ClearKeys();
        using var s = _db.NewScope();
        Assert.Equal(0, (await NewRepo(s).GetLastActivePair()).Id);
        Assert.Empty(await NewRepo(s).GetCurrentActiveKeysAsync());
    }

    [Fact]
    public async Task CleanupExpiredKeys_RetiredInThePast_IsNoLongerCurrent_ButFutureOnesStay()
    {
        await ClearKeys();
        try
        {
            var expired = await Rotate();
            using (var s = _db.NewScope())
            {
                // a second, still-valid key that is also marked current with a future retirement
                s.Context.SigningKeys.Add(new SigningKey(0, "future-kid", "pub", "priv") { RetiredAt = DateTimeOffset.UtcNow.AddDays(1) });
                await s.Context.SaveChangesAsync();
                await s.Context.SigningKeys.Where(k => k.Kid == expired)
                    .ExecuteUpdateAsync(u => u.SetProperty(k => k.RetiredAt, DateTimeOffset.UtcNow.AddDays(-1)));
            }
            using var s2 = _db.NewScope();
            await NewRepo(s2).CleanupExpiredKeys();
            var current = await NewRepo(s2).GetCurrentActiveKeysAsync();
            Assert.Equal("future-kid", Assert.Single(current).Kid);
        }
        finally { await ClearKeys(); }
    }

    // N simultaneous rotations, each in its own scope (e.g. several app instances). Whatever the interleaving,
    // exactly one key must be current afterwards and every rotation must have stored its key.
    [Theory]
    [InlineData(5)]
    public async Task ConcurrentRotations_LeaveExactlyOneCurrentKey(int tasks)
    {
        await ClearKeys();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, tasks).Select(_ => Task.Run(Rotate)));
            using var s = _db.NewScope();
            Assert.Equal(tasks, await s.Context.SigningKeys.CountAsync());
            Assert.Equal(1, await s.Context.SigningKeys.CountAsync(k => k.IsCurrent));
        }
        finally { await ClearKeys(); }
    }

    public void Dispose() => _db.Dispose();
}
