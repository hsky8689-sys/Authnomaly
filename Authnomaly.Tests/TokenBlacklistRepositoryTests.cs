using System.Collections.Concurrent;
using System.Globalization;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Repositories.MemoryRepositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Authnomaly.Tests;

// Real Redis (localhost:6379, the docker container), real RedisCache as IDistributedCache.
// Every key the tests create lives under the "authnomalytest:" prefix and is removed in each test's finally.
[Collection("Redis")]
public class TokenBlacklistRepositoryTests : IDisposable
{
    private const string Endpoint = "localhost:6379";
    private const string Prefix = "authnomalytest:";
    private readonly ConcurrentBag<RedisCache> _caches = new();

    // Each call = a separate cache with its own Redis connection, like a separate app instance
    private RedisCache NewCache()
    {
        var cache = new RedisCache(Options.Create(new RedisCacheOptions { Configuration = Endpoint, InstanceName = Prefix }));
        _caches.Add(cache);
        return cache;
    }

    private ITokenBlacklistStore NewRepo() => new TokenBlacklistRepository(NewCache());

    private static async Task CleanupAsync()
    {
        using var mux = await ConnectionMultiplexer.ConnectAsync(Endpoint);
        var db = mux.GetDatabase();
        foreach (var ep in mux.GetEndPoints())
        {
            var keys = mux.GetServer(ep).Keys(pattern: Prefix + "*").ToArray();
            if (keys.Length > 0) await db.KeyDeleteAsync(keys);
        }
    }

    private static string NewJti() => Guid.NewGuid().ToString();

    // ---------- blacklist: obvious cases ----------

    // ttl is passed as 60, i.e. 60 seconds
    [Theory]
    [InlineData(BlacklistLevel.Expired)]
    [InlineData(BlacklistLevel.AdminEnforced)]
    [InlineData(BlacklistLevel.AnomalyDetected)]
    [InlineData(BlacklistLevel.Logout)]
    public async Task AddToBlacklist_ThenIsBlacklisted_ReturnsTheSameReason(BlacklistLevel reason)
    {
        try
        {
            var repo = NewRepo();
            var jti = NewJti();
            Assert.True(await repo.AddToBlacklist(jti, 60, reason));
            Assert.Equal(reason, await repo.IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task IsBlacklisted_UnknownJti_ReturnsNotBlacklisted()
    {
        try
        {
            Assert.Equal(BlacklistLevel.NotBlacklisted, await NewRepo().IsBlacklisted(NewJti()));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task AddToBlacklist_NotBlacklistedReason_ReturnsFalseAndStoresNothing()
    {
        try
        {
            var repo = NewRepo();
            var jti = NewJti();
            Assert.False(await repo.AddToBlacklist(jti, 60, BlacklistLevel.NotBlacklisted));
            Assert.Equal(BlacklistLevel.NotBlacklisted, await repo.IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task IsBlacklisted_OnlyTheBlacklistedJtiIsAffected()
    {
        try
        {
            var repo = NewRepo();
            var bad = NewJti();
            var good = NewJti();
            Assert.True(await repo.AddToBlacklist(bad, 60, BlacklistLevel.Logout));
            Assert.Equal(BlacklistLevel.Logout, await repo.IsBlacklisted(bad));
            Assert.Equal(BlacklistLevel.NotBlacklisted, await repo.IsBlacklisted(good));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task IsBlacklisted_ValueThatIsNotALevel_ReturnsNotBlacklisted()
    {
        try
        {
            var jti = NewJti();
            await NewCache().SetStringAsync($"blacklist:{jti}", "not-a-level");
            Assert.Equal(BlacklistLevel.NotBlacklisted, await NewRepo().IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    // ---------- blacklist: expiration ----------

    [Fact]
    public async Task AddToBlacklist_EntryDisappearsAfterItsTtl()
    {
        try
        {
            var repo = NewRepo();
            var jti = NewJti();
            Assert.True(await repo.AddToBlacklist(jti, 2, BlacklistLevel.Logout));
            Assert.Equal(BlacklistLevel.Logout, await repo.IsBlacklisted(jti));
            await Task.Delay(TimeSpan.FromSeconds(3.5));
            Assert.Equal(BlacklistLevel.NotBlacklisted, await repo.IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task AddToBlacklist_LongTtl_StillBlacklistedAfterAShortWait()
    {
        try
        {
            var repo = NewRepo();
            var jti = NewJti();
            Assert.True(await repo.AddToBlacklist(jti, 300, BlacklistLevel.AdminEnforced));
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            Assert.Equal(BlacklistLevel.AdminEnforced, await repo.IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task AddToBlacklist_SameJtiAgain_OverwritesTheReason()
    {
        try
        {
            var repo = NewRepo();
            var jti = NewJti();
            Assert.True(await repo.AddToBlacklist(jti, 60, BlacklistLevel.Expired));
            Assert.True(await repo.AddToBlacklist(jti, 60, BlacklistLevel.AnomalyDetected));
            Assert.Equal(BlacklistLevel.AnomalyDetected, await repo.IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    // ---------- refresh-token families: obvious cases ----------

    [Fact]
    public async Task SetCurrentFamilyJti_ThenGet_ReturnsTheJti()
    {
        try
        {
            var repo = NewRepo();
            var family = Guid.NewGuid();
            var jti = NewJti();
            await repo.SetCurrentFamilyJti(family, jti, TimeSpan.FromMinutes(1));
            Assert.Equal(jti, await repo.GetCurrentFamilyJti(family));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task GetCurrentFamilyJti_UnknownFamily_ReturnsNull()
    {
        try
        {
            Assert.Null(await NewRepo().GetCurrentFamilyJti(Guid.NewGuid()));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task SetCurrentFamilyJti_Rotation_LatestJtiWins()
    {
        try
        {
            var repo = NewRepo();
            var family = Guid.NewGuid();
            var second = NewJti();
            await repo.SetCurrentFamilyJti(family, NewJti(), TimeSpan.FromMinutes(1));
            await repo.SetCurrentFamilyJti(family, second, TimeSpan.FromMinutes(1));
            Assert.Equal(second, await repo.GetCurrentFamilyJti(family));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task SetCurrentFamilyJti_FamiliesDoNotInterfere()
    {
        try
        {
            var repo = NewRepo();
            var (fa, fb, ja, jb) = (Guid.NewGuid(), Guid.NewGuid(), NewJti(), NewJti());
            await repo.SetCurrentFamilyJti(fa, ja, TimeSpan.FromMinutes(1));
            await repo.SetCurrentFamilyJti(fb, jb, TimeSpan.FromMinutes(1));
            Assert.Equal(ja, await repo.GetCurrentFamilyJti(fa));
            Assert.Equal(jb, await repo.GetCurrentFamilyJti(fb));
        }
        finally { await CleanupAsync(); }
    }

    // ---------- refresh-token families: expiration ----------

    [Fact]
    public async Task SetCurrentFamilyJti_EntryDisappearsAfterItsTtl()
    {
        try
        {
            var repo = NewRepo();
            var family = Guid.NewGuid();
            await repo.SetCurrentFamilyJti(family, NewJti(), TimeSpan.FromSeconds(2));
            Assert.NotNull(await repo.GetCurrentFamilyJti(family));
            await Task.Delay(TimeSpan.FromSeconds(3.5));
            Assert.Null(await repo.GetCurrentFamilyJti(family));
        }
        finally { await CleanupAsync(); }
    }

    // ---------- refresh-token families: revocation ----------

    [Fact]
    public async Task RevokeFamily_RemovesTheCurrentJti()
    {
        try
        {
            var repo = NewRepo();
            var family = Guid.NewGuid();
            await repo.SetCurrentFamilyJti(family, NewJti(), TimeSpan.FromMinutes(1));
            await repo.RevokeFamily(family);
            Assert.Null(await repo.GetCurrentFamilyJti(family));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task RevokeFamily_UnknownFamily_DoesNotThrow()
    {
        try
        {
            await NewRepo().RevokeFamily(Guid.NewGuid());
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task RevokeFamily_OnlyAffectsThatFamily()
    {
        try
        {
            var repo = NewRepo();
            var (revoked, kept, keptJti) = (Guid.NewGuid(), Guid.NewGuid(), NewJti());
            await repo.SetCurrentFamilyJti(revoked, NewJti(), TimeSpan.FromMinutes(1));
            await repo.SetCurrentFamilyJti(kept, keptJti, TimeSpan.FromMinutes(1));
            await repo.RevokeFamily(revoked);
            Assert.Equal(keptJti, await repo.GetCurrentFamilyJti(kept));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task IsFamilyRevoked_ActiveFamily_ReturnsFalse()
    {
        try
        {
            var repo = NewRepo();
            var family = Guid.NewGuid();
            await repo.SetCurrentFamilyJti(family, NewJti(), TimeSpan.FromMinutes(1));
            Assert.False(await repo.IsFamilyRevoked(family));
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task IsFamilyRevoked_AfterRevokeFamily_ReturnsTrue()
    {
        try
        {
            var repo = NewRepo();
            var family = Guid.NewGuid();
            await repo.SetCurrentFamilyJti(family, NewJti(), TimeSpan.FromMinutes(1));
            await repo.RevokeFamily(family);
            Assert.True(await repo.IsFamilyRevoked(family));
        }
        finally { await CleanupAsync(); }
    }

    // ---------- last location ----------

    [Fact]
    public async Task GetLastLocation_NothingSaved_ReturnsNullCoordinates()
    {
        try
        {
            var result = await NewRepo().GetLastLocation("refresh", $"user_{Guid.NewGuid():N}");
            Assert.Null(result.latitude);
            Assert.Null(result.longitude);
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task GetLastLocation_SavedValue_IsParsedBack()
    {
        try
        {
            var user = $"user_{Guid.NewGuid():N}";
            var when = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);
            var saved = string.Join("/",
                44.4268.ToString(CultureInfo.InvariantCulture),
                26.1025.ToString(CultureInfo.InvariantCulture),
                when.ToString("O", CultureInfo.InvariantCulture));
            await NewCache().SetStringAsync($"last:refresh:{user}", saved);

            var result = await NewRepo().GetLastLocation("refresh", user);
            Assert.Equal(44.4268, result.latitude);
            Assert.Equal(26.1025, result.longitude);
            Assert.Equal(when, result.timestamp);
        }
        finally { await CleanupAsync(); }
    }

    [Fact]
    public async Task GetLastLocation_ExpiredValue_ReturnsNullCoordinates()
    {
        try
        {
            var user = $"user_{Guid.NewGuid():N}";
            var saved = $"44.4268/26.1025/{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}";
            await NewCache().SetStringAsync($"last:refresh:{user}", saved,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2) });
            Assert.NotNull((await NewRepo().GetLastLocation("refresh", user)).latitude);
            await Task.Delay(TimeSpan.FromSeconds(3.5));
            Assert.Null((await NewRepo().GetLastLocation("refresh", user)).latitude);
        }
        finally { await CleanupAsync(); }
    }

    // ---------- concurrency: N tasks, each with its own cache/connection (like N app instances) ----------

    [Theory]
    [InlineData(20)]
    public async Task ConcurrentAddToBlacklist_DistinctJtis_AllEndUpBlacklisted(int tasks)
    {
        try
        {
            var jtis = Enumerable.Range(0, tasks).Select(_ => NewJti()).ToList();
            var results = await Task.WhenAll(jtis.Select(j => Task.Run(() => NewRepo().AddToBlacklist(j, 60, BlacklistLevel.Logout))));
            Assert.All(results, Assert.True);
            var checker = NewRepo();
            foreach (var jti in jtis) Assert.Equal(BlacklistLevel.Logout, await checker.IsBlacklisted(jti));
        }
        finally { await CleanupAsync(); }
    }

    [Theory]
    [InlineData(20)]
    public async Task ConcurrentAddToBlacklist_SameJti_EndsWithOneOfTheReasons(int tasks)
    {
        try
        {
            var jti = NewJti();
            var reasons = new[] { BlacklistLevel.Expired, BlacklistLevel.AdminEnforced, BlacklistLevel.AnomalyDetected, BlacklistLevel.Logout };
            await Task.WhenAll(Enumerable.Range(0, tasks).Select(i =>
                Task.Run(() => NewRepo().AddToBlacklist(jti, 60, reasons[i % reasons.Length]))));
            Assert.Contains(await NewRepo().IsBlacklisted(jti), reasons);
        }
        finally { await CleanupAsync(); }
    }

    [Theory]
    [InlineData(20)]
    public async Task ConcurrentSetCurrentFamilyJti_SameFamily_EndsWithOneOfTheValues(int tasks)
    {
        try
        {
            var family = Guid.NewGuid();
            var jtis = Enumerable.Range(0, tasks).Select(_ => NewJti()).ToList();
            await Task.WhenAll(jtis.Select(j => Task.Run(() => NewRepo().SetCurrentFamilyJti(family, j, TimeSpan.FromMinutes(1)))));
            Assert.Contains(await NewRepo().GetCurrentFamilyJti(family), jtis);
        }
        finally { await CleanupAsync(); }
    }

    [Theory]
    [InlineData(20)]
    public async Task ConcurrentRevokeFamily_SameFamily_NoErrorsAndFamilyIsGone(int tasks)
    {
        try
        {
            var family = Guid.NewGuid();
            await NewRepo().SetCurrentFamilyJti(family, NewJti(), TimeSpan.FromMinutes(1));
            await Task.WhenAll(Enumerable.Range(0, tasks).Select(_ => Task.Run(() => NewRepo().RevokeFamily(family))));
            Assert.Null(await NewRepo().GetCurrentFamilyJti(family));
        }
        finally { await CleanupAsync(); }
    }

    // A refresh (Set) racing a revoke: whichever lands last wins, but the store must stay consistent
    // (either the new jti is there, or nothing is) and nothing may throw.
    [Theory]
    [InlineData(20)]
    public async Task ConcurrentSetAndRevoke_SameFamily_EndsConsistent(int tasks)
    {
        try
        {
            var family = Guid.NewGuid();
            var jtis = Enumerable.Range(0, tasks).Select(_ => NewJti()).ToList();
            await Task.WhenAll(jtis.SelectMany(j => new[]
            {
                Task.Run(() => NewRepo().SetCurrentFamilyJti(family, j, TimeSpan.FromMinutes(1))),
                Task.Run(() => NewRepo().RevokeFamily(family))
            }));
            var current = await NewRepo().GetCurrentFamilyJti(family);
            Assert.True(current is null || jtis.Contains(current));
        }
        finally { await CleanupAsync(); }
    }

    public void Dispose()
    {
        foreach (var cache in _caches) cache.Dispose();
    }
}
