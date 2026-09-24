using System.Net;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Xunit;

namespace Authnomaly.Tests;

[Collection("Database")]
public class LoginAttemptsRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();

    private static LoginAttempt NewAttempt(string username) => new(0)
    {
        Username = username,
        ApplicationId = 1,
        DeviceName = "test-device",
        AttemptTime = DateTimeOffset.UtcNow,
        Country = "Romania",
        City = "Targu Jiu",
        Browser = "test-browser",
        OperatingSystem = "test-os",
        Succeeded = true,
        IpAddress = IPAddress.Parse("203.0.113.7"),
        Port = 443
    };

    [Fact]
    public async Task Add_ThenFindById_ReturnsSameData()
    {
        var name = $"attempt_{Guid.NewGuid():N}";
        long id;
        using (var s = _db.NewScope())
            id = await new LoginAttemptsRepository(s.Context).Add(NewAttempt(name));
        try
        {
            Assert.True(id > 0);
            using var s = _db.NewScope();
            var found = await new LoginAttemptsRepository(s.Context).FindById(id);
            Assert.Equal(id, found.Id);
            Assert.Equal(name, found.Username);
            Assert.Equal("Romania", found.Country);
            Assert.Equal("Targu Jiu", found.City);
            Assert.Equal(IPAddress.Parse("203.0.113.7"), found.IpAddress);
            Assert.Equal(443, found.Port);
            Assert.True(found.Succeeded);
        }
        finally
        {
            using var s = _db.NewScope();
            await new LoginAttemptsRepository(s.Context).Delete(id);
        }
    }

    [Fact]
    public async Task FindById_UnknownId_ReturnsIdZeroSentinel()
    {
        using var s = _db.NewScope();
        var found = await new LoginAttemptsRepository(s.Context).FindById(-12345);
        Assert.Equal(0, found.Id);
    }

    [Fact]
    public async Task Add_WithNullTimeAndNullFields_UsesEntityDefaults()
    {
        var attempt = new LoginAttempt(0) { Username = $"attempt_{Guid.NewGuid():N}", AttemptTime = null, Country = null, City = null };
        long id;
        using (var s = _db.NewScope())
            id = await new LoginAttemptsRepository(s.Context).Add(attempt);
        try
        {
            Assert.True(id > 0);
            using var s = _db.NewScope();
            var found = await new LoginAttemptsRepository(s.Context).FindById(id);
            Assert.Equal("N/A", found.Country);
            Assert.Equal("N/A", found.City);
            Assert.NotNull(found.AttemptTime);
        }
        finally
        {
            using var s = _db.NewScope();
            await new LoginAttemptsRepository(s.Context).Delete(id);
        }
    }

    [Fact]
    public async Task Delete_ExistingThenAgain_TrueThenFalse()
    {
        long id;
        using (var s = _db.NewScope())
            id = await new LoginAttemptsRepository(s.Context).Add(NewAttempt($"attempt_{Guid.NewGuid():N}"));
        using (var s = _db.NewScope())
        {
            var repo = new LoginAttemptsRepository(s.Context);
            Assert.True(await repo.Delete(id));
            Assert.False(await repo.Delete(id));
            Assert.Equal(0, (await repo.FindById(id)).Id);
        }
    }

    [Theory]
    [InlineData(10)]
    public async Task ConcurrentAdds_EachWithOwnScope_AllSucceedWithDistinctIds(int tasks)
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();
        var work = Enumerable.Range(0, tasks).Select(i => Task.Run(async () =>
        {
            using var s = _db.NewScope();
            ILoginAttemptsRepo repo = new LoginAttemptsRepository(s.Context);
            ids.Add(await repo.Add(NewAttempt($"attempt_{Guid.NewGuid():N}")));
        }));
        try
        {
            await Task.WhenAll(work);
            Assert.Equal(tasks, ids.Count);
            Assert.Equal(tasks, ids.Distinct().Count());
            Assert.All(ids, id => Assert.True(id > 0));
        }
        finally
        {
            foreach (var id in ids)
            {
                using var s = _db.NewScope();
                await new LoginAttemptsRepository(s.Context).Delete(id);
            }
        }
    }

    public void Dispose() => _db.Dispose();
}
