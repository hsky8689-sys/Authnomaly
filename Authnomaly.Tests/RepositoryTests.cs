using Authnomaly.Domain;
using Authnomaly.Repositories;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public class RepositoryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private AuthnomalyDatabaseContext _context;
    private IUsersRepo _usersRepository;

    public RepositoryTests(ITestOutputHelper output)
    {
        _output = output;
        DbContextOptionsBuilder<AuthnomalyDatabaseContext> options = new DbContextOptionsBuilder<AuthnomalyDatabaseContext>();
        var password = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD") ?? throw new InvalidOperationException("AUTHNOMALY_DB_PASSWORD environment variable is not set.");;
        options.UseNpgsql($"Host=localhost;Database=authnomalytest;Username=postgres;Password={password}");
        _context = new AuthnomalyDatabaseContext(options.Options);
        _usersRepository = new UsersRepository(_context);
    }
    // Inserts `count` freshly-generated users straight into the repo (real Add() calls, no mocking)
    // and hands back the User instances the repo returned - each with the real, DB-assigned Id -
    // so a test can immediately use them (FindById, Delete, etc.) without re-querying.
    private  async Task<IList<User>> PopulateRepo(int count = 10)
    {
        var created = new List<User>();
        for (int i = 0; i < count; i++)
        {
            var username = $"testuser_{Guid.NewGuid():N}";
            var email = $"{username}@example.com";
            long id = await _usersRepository.Add(new User(0, username, email));
            created.Add(new User(id, username, email));
        }
        return created;
    }
    private async Task DeleteUsersFromRepo(IList<User> oldUsers)
    {
        foreach (User who in oldUsers)
        {
            await _usersRepository.Delete(who.Id);
        }
    }
    [Theory]
    [InlineData(20)]
    public async Task UsersRepositoryBasicTests(int count)
    {
        IList<User> added = await PopulateRepo(count);
        long firstId = added[0].Id;
        long lastId = added[added.Count - 1].Id;
        for (long wrongId = firstId; wrongId <= lastId; wrongId++)
            await Assert.ThrowsAsync<InvalidOperationException>(
                async ()=> await _usersRepository.Add(new User(wrongId, "", ""))
                );
        _context.ChangeTracker.Clear();
        for (long wrongId = firstId; wrongId <= lastId; wrongId++)
            Assert.True((await _usersRepository.FindById(wrongId)).Id != 0);
        var newUser = new User(0, "ewqeqweqweqwe", "312312312312312");
        var firstBiggerId = await _usersRepository.Add(newUser);
        Assert.True(firstBiggerId==1+lastId);
        var deleted = await _usersRepository.Delete(firstBiggerId);
        Assert.True(deleted);
        var newUser2 = new User(lastId+1,"","");
        _context.ChangeTracker.Clear();
        Assert.True((await _usersRepository.Add(newUser2)) == firstBiggerId);//entities with former id's can be added tho...
        Assert.True(await _usersRepository.Delete(firstBiggerId));
        await DeleteUsersFromRepo(added);
        _context.ChangeTracker.Clear();
        for (long wrongId = firstId; wrongId <= lastId; wrongId++)
            Assert.True((await _usersRepository.FindById(wrongId)).Id == 0); 
    }
    public void Dispose()
    {
        _context.Dispose();
    }
}