using System.Collections.Concurrent;
using System.Security.Cryptography;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Utils;
using Xunit;

namespace Authnomaly.Tests;

[Collection("Database")]
public class CredentialsRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    // Same order AuthService.Authenticate uses: add the user, then the credentials that point at it
    private static async Task<long> RegisterAsync(TestScope s, string username, string password)
    {
        var user = new User(0, username, $"{username}@example.com");
        long userId = await new UsersRepository(s.Context).Add(user);
        if (userId == 0) return 0;
        var hashed = Encryption.HashPassword(password);
        var creds = new AuthCredentials(0, Convert.ToBase64String(hashed.Hash), user, hashed.Salt);
        return await new CredentialsRepository(s.Context).Add(creds) == 0 ? 0 : userId;
    }

    private async Task<long> RegisterNewUser(string password = "OldPassw0rd!")
    {
        using var s = _db.NewScope();
        return await RegisterAsync(s, $"cred_{Guid.NewGuid():N}", password);
    }

    // deleting the user removes its credentials too (cascade)
    private async Task CleanupUser(long userId)
    {
        using var s = _db.NewScope();
        await new UsersRepository(s.Context).Delete(userId);
    }

    private async Task<User> GetById(long id)
    {
        using var s = _db.NewScope();
        return await new UsersRepository(s.Context).FindById(id);
    }
    
    [Fact]
    public async Task Add_ThenFindByUserId_AndFindById_ReturnSameData()
    {
        var name = $"cred_{Guid.NewGuid():N}";
        long userId;
        using (var s = _db.NewScope()) userId = await RegisterAsync(s, name, "Passw0rd!x");
        try
        {
            Assert.True(userId > 0);
            using var s = _db.NewScope();
            var repo = new CredentialsRepository(s.Context);
            var byUser = await repo.FindByUserId(userId);
            var byId = await repo.FindById(userId);
            Assert.Equal(userId, byUser.Id);
            Assert.Equal(userId, byId.Id);
            Assert.Equal(byUser.PasswordHash, byId.PasswordHash);
            Assert.NotEmpty(byUser.Salt);
            Assert.True(Encryption.VerifyPassword("Passw0rd!x", Convert.FromBase64String(byUser.PasswordHash), byUser.Salt));
            Assert.False(Encryption.VerifyPassword("WrongPass", Convert.FromBase64String(byUser.PasswordHash), byUser.Salt));
        }
        finally { await CleanupUser(userId); }
    }

    [Fact]
    public async Task FindByUserId_AndFindById_UnknownId_ReturnZeroSentinel()
    {
        using var s = _db.NewScope();
        var repo = new CredentialsRepository(s.Context);
        Assert.Equal(0, (await repo.FindByUserId(-12345)).Id);
        Assert.Equal(0, (await repo.FindById(-12345)).Id);
    }

    [Fact]
    public async Task ChangePassword_ExistingUser_StoresNewHashAndSalt()
    {
        long userId = await RegisterNewUser("OldPassw0rd!");
        try
        {
            (byte[] newPasswordHash, byte[] newPasswordSalt) newPassData = Encryption.HashPassword("newPasswOrd!"); 
            AuthCredentials before;
            using (var s = _db.NewScope()) before = await new CredentialsRepository(s.Context).FindById(userId);

            using (var s = _db.NewScope())
                Assert.True(await new CredentialsRepository(s.Context).ChangePassword(userId, Convert.ToBase64String(newPassData.newPasswordHash),newPassData.newPasswordSalt,before.PasswordHash,before.Salt));

            using var s2 = _db.NewScope();
            var after = await new CredentialsRepository(s2.Context).FindById(userId);
            Assert.NotEqual(before.PasswordHash, after.PasswordHash);
            Assert.NotEqual(before.Salt, after.Salt);
            Assert.True(Encryption.VerifyPassword("NewPassw0rd!", Convert.FromBase64String(after.PasswordHash), after.Salt));
            Assert.False(Encryption.VerifyPassword("OldPassw0rd!", Convert.FromBase64String(after.PasswordHash), after.Salt));
        }
        finally { await CleanupUser(userId); }
    }

    [Fact]
    public async Task ChangePassword_UnknownUser_ReturnsFalse()
    {
        using var s = _db.NewScope();
        (byte[] newPasswordHash, byte[] newPasswordSalt) newPassData = Encryption.HashPassword("newPasswOrd!");
        AuthCredentials oldCreds = new AuthCredentials(-12345);
        oldCreds.PasswordHash = "e1e1312312312";
        oldCreds.Salt = RandomNumberGenerator.GetBytes(14);
        Assert.False(await new CredentialsRepository(s.Context).ChangePassword(-12345, 
            Convert.ToBase64String(newPassData.newPasswordHash),
            newPassData.newPasswordSalt,
            oldCreds.PasswordHash,
            oldCreds.Salt));
    }

    [Fact]
    public async Task ChangeUsername_ExistingUser_Persists()
    {
        long userId = await RegisterNewUser();
        var newName = $"renamed_{Guid.NewGuid():N}";
        try
        {
            using (var s = _db.NewScope())
            {
                var user = await new UsersRepository(s.Context).FindById(userId); 
                Assert.True(await new UsersRepository(s.Context).ChangeUsername(userId, user.Username,newName));
            }

            using var s2 = _db.NewScope();
            Assert.Equal(newName, (await new UsersRepository(s2.Context).FindById(userId)).Username);
        }
        finally { await CleanupUser(userId); }
    }

    [Fact]
    public async Task ChangeUsername_UnknownUser_ReturnsFalse()
    {
        using var s = _db.NewScope();
        Assert.False(await new UsersRepository(s.Context).ChangeUsername(-12345, "whatever","idc"));
    }

    [Fact]
    public async Task Delete_ExistingThenAgain_TrueThenFalse()
    {
        long userId = await RegisterNewUser();
        try
        {
            using var s = _db.NewScope();
            var repo = new CredentialsRepository(s.Context);
            Assert.True(await repo.Delete(userId));
            Assert.False(await repo.Delete(userId));
            Assert.Equal(0, (await repo.FindById(userId)).Id);
        }
        finally { await CleanupUser(userId); }
    }

    [Theory]
    [InlineData(10)]
    public async Task ConcurrentRegistrations_DifferentUsers_AllSucceed(int tasks)
    {
        var userIds = new ConcurrentBag<long>();
        var work = Enumerable.Range(0, tasks).Select(i => Task.Run(async () =>
        {
            using var s = _db.NewScope();
            userIds.Add(await RegisterAsync(s, $"cred_{Guid.NewGuid():N}", "Passw0rd!x"));
        }));
        try
        {
            await Task.WhenAll(work);
            Assert.Equal(tasks, userIds.Distinct().Count());
            using var s = _db.NewScope();
            var repo = new CredentialsRepository(s.Context);
            foreach (var id in userIds) Assert.Equal(id, (await repo.FindByUserId(id)).Id);
        }
        finally { foreach (var id in userIds) await CleanupUser(id); }
    }

    // Simulates N simultaneous registrations of the SAME username, each in its own scope like separate HTTP
    // requests. The unique index on Users.Username must let exactly one through.
    [Theory]
    [InlineData(10)]
    public async Task ConcurrentRegistrations_SameUsername_OnlyOneSucceeds(int tasks)
    {
        var name = $"cred_{Guid.NewGuid():N}";
        int succeeded = 0;
        var work = Enumerable.Range(0, tasks).Select(i => Task.Run(async () =>
        {
            using var s = _db.NewScope();
            if (await RegisterAsync(s, name, "Passw0rd!x") != 0) Interlocked.Increment(ref succeeded);
        }));
        try
        {
            await Task.WhenAll(work);
            Assert.Equal(1, succeeded);
            using var s = _db.NewScope();
            var user = await new UsersRepository(s.Context).FindByUsername(name);
            Assert.NotEqual(0, user.Id);
            Assert.Equal(user.Id, (await new CredentialsRepository(s.Context).FindByUserId(user.Id)).Id);
        }
        finally
        {
            using var s = _db.NewScope();
            var user = await new UsersRepository(s.Context).FindByUsername(name);
            if (user.Id != 0) await new UsersRepository(s.Context).Delete(user.Id);
        }
    }

    // Many contexts renaming the same credentials at once: no exception,
    // last writer wins, final value is one of the inputs
    [Theory]
    [InlineData(10)]
    public async Task ConcurrentChangeUsername_SameUser_EndsWithOneOfTheValues(int tasks)
    {
        long userId = await RegisterNewUser();
        var names = Enumerable.Range(0, tasks).Select(i => $"renamed_{i}_{Guid.NewGuid():N}").ToList();
        try
        {
            await Task.WhenAll(names.Select(n => Task.Run(async () =>
            {
                using var s = _db.NewScope();
                var user = await new UsersRepository(s.Context).FindById(userId);
                await new UsersRepository(s.Context).ChangeUsername(userId, user.Username,n);
            })));
            using var s2 = _db.NewScope();
            Assert.Contains((await new UsersRepository(s2.Context).FindById(userId)).Username, names);
        }
        finally { await CleanupUser(userId); }
    }

    public void Dispose() => _db.Dispose();
}
