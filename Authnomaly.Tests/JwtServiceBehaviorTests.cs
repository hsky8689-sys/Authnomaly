using System.Collections.Concurrent;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Repositories.MemoryRepositories;
using Authnomaly.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

// Real Postgres (authnomalytest) + real Redis, no mocks: JwtService only orchestrates the two stores,
// so these tests cover what JwtService itself adds. Redis keys use their own prefix so they never
// collide with TokenBlacklistRepositoryTests' cleanup.
[Collection("Database")]
public class JwtServiceBehaviorTests : IDisposable
{
    private const string RedisEndpoint = "localhost:6379";
    private const string RedisPrefix = "authnomalytest-jwtservice:";

    private readonly ITestOutputHelper _output;
    private readonly TestDb _db = new();
    private readonly DataProtectionAPIService _protection = new(new EphemeralDataProtectionProvider());
    private readonly ConcurrentBag<RedisCache> _caches = new();

    public JwtServiceBehaviorTests(ITestOutputHelper output) => _output = output;

    private JwtService NewService(TestScope scope)
    {
        var cache = new RedisCache(Options.Create(new RedisCacheOptions { Configuration = RedisEndpoint, InstanceName = RedisPrefix }));
        _caches.Add(cache);
        return new JwtService(new SigningKeysRepository(scope.Context, _protection), new TokenBlacklistRepository(cache));
    }

    private async Task ClearKeys()
    {
        using var s = _db.NewScope();
        await s.Context.SigningKeys.ExecuteDeleteAsync();
    }

    private static async Task ClearRedis()
    {
        using var mux = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var db = mux.GetDatabase();
        foreach (var ep in mux.GetEndPoints())
        {
            var keys = mux.GetServer(ep).Keys(pattern: RedisPrefix + "*").ToArray();
            if (keys.Length > 0) await db.KeyDeleteAsync(keys);
        }
    }

    // ---------- signing keys ----------

    [Fact]
    public async Task GetLastPrivateKey_NoKeysYet_CreatesOneCurrentKey()
    {
        await ClearKeys();
        try
        {
            using var s = _db.NewScope();
            var key = await NewService(s).GetLastPrivateKey();

            Assert.True(key.Id != 0);
            Assert.True(key.IsCurrent);
            Assert.Equal(1, await s.Context.SigningKeys.CountAsync());
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task GetLastPrivateKey_CalledTwice_ReturnsTheSameKeyAndCreatesNoMore()
    {
        await ClearKeys();
        try
        {
            using var s = _db.NewScope();
            var service = NewService(s);
            var first = await service.GetLastPrivateKey();
            var second = await service.GetLastPrivateKey();

            Assert.Equal(first.Kid, second.Kid);
            Assert.Equal(1, await s.Context.SigningKeys.CountAsync());
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task GetActiveSigningKeys_NoKeysYet_BootstrapsExactlyOne()
    {
        await ClearKeys();
        try
        {
            using var s = _db.NewScope();
            var active = await NewService(s).GetActiveSigningKeys();

            var only = Assert.Single(active);
            Assert.True(only.IsCurrent);
        }
        finally { await ClearKeys(); }
    }

    [Fact]
    public async Task GetActiveSigningKeys_KeyAlreadyExists_DoesNotCreateAnother()
    {
        await ClearKeys();
        try
        {
            using var s = _db.NewScope();
            var service = NewService(s);
            var existing = await service.GetLastPrivateKey();
            var active = await service.GetActiveSigningKeys();

            Assert.Equal(existing.Kid, Assert.Single(active).Kid);
            Assert.Equal(1, await s.Context.SigningKeys.CountAsync());
        }
        finally { await ClearKeys(); }
    }
    
    [Theory]
    [InlineData(20)]
    public async Task GetLastPrivateKey_ConcurrentFirstCalls_CreateOneKey_LeaveOneCurrent_AndNeverReturnAnEmptyKey(int callers)
    {
        await ClearKeys();
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
            {
                using var s = _db.NewScope();
                return await NewService(s).GetLastPrivateKey();
            })));

            using var check = _db.NewScope();
            var total = await check.Context.SigningKeys.CountAsync();
            var current = await check.Context.SigningKeys.CountAsync(k => k.IsCurrent);
            _output.WriteLine($@"keys created={total},
                                 current={current}, empty keys handed 
                                 out={results.Count(r => r.Id == 0)}");
            Assert.Equal(1, total);
            Assert.Equal(1, current);
            Assert.All(results, r => Assert.True(r.Id != 0));
        }
        finally { await ClearKeys(); }
    }

    // ---------- last location guards ----------

    [Theory]
    [InlineData("", "someone")]
    [InlineData("refresh", "")]
    public async Task GetLastLocation_EmptyEndpointOrUsername_ReturnsDefault(string endpoint, string username)
    {
        using var s = _db.NewScope();
        var result = await NewService(s).GetLastLocation(endpoint, username);

        Assert.Null(result.latitude);
        Assert.Null(result.longitude);
        Assert.Equal(default, result.timestamp);
    }

    [Fact]
    public async Task GetLastLocation_NothingSavedForThatUser_ReturnsNullCoordinates()
    {
        using var s = _db.NewScope();
        var result = await NewService(s).GetLastLocation("refresh", $"user_{Guid.NewGuid():N}");

        Assert.Null(result.latitude);
        Assert.Null(result.longitude);
    }

    // ---------- token families (Redis) ----------

    [Fact]
    public async Task Family_SetThenGetThenInvalidate_FollowsTheLifecycle()
    {
        try
        {
            using var s = _db.NewScope();
            var service = NewService(s);
            var familyId = Guid.NewGuid();
            var jti = Guid.NewGuid().ToString();

            await service.SetCurrentFamilyJti(familyId, jti, TimeSpan.FromMinutes(1));
            Assert.Equal(jti, await service.GetCurrentFamilyOfJti(familyId.ToString()));

            Assert.True(await service.InvalidateTokenFamily(familyId.ToString(), BlacklistLevel.AnomalyDetected));
            Assert.Null(await service.GetCurrentFamilyOfJti(familyId.ToString()));
        }
        finally { await ClearRedis(); }
    }

    [Fact]
    public async Task InvalidateTokenFamily_UnknownFamily_ReturnsFalse()
    {
        try
        {
            using var s = _db.NewScope();

            Assert.False(await NewService(s).InvalidateTokenFamily(Guid.NewGuid().ToString(), BlacklistLevel.Logout));
        }
        finally { await ClearRedis(); }
    }

    // Characterization: both methods Guid.Parse the string they are given, so a value that is not a GUID
    // throws FormatException (a 500 if it comes straight from a request body). Change these two tests if
    // you decide malformed input should return null / false instead.
    [Fact]
    public async Task GetCurrentFamilyOfJti_NotAGuid_Throws()
    {
        using var s = _db.NewScope();

        await Assert.ThrowsAsync<FormatException>(() => NewService(s).GetCurrentFamilyOfJti("not-a-guid"));
    }

    [Fact]
    public async Task InvalidateTokenFamily_NotAGuid_Throws()
    {
        using var s = _db.NewScope();

        await Assert.ThrowsAsync<FormatException>(() => NewService(s).InvalidateTokenFamily("not-a-guid", BlacklistLevel.Logout));
    }

    public void Dispose()
    {
        foreach (var cache in _caches) cache.Dispose();
        _db.Dispose();
    }
}
