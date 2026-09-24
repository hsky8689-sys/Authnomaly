using Authnomaly.Domain;
using Authnomaly.Repositories;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

[Collection("Database")]
public class RepositoryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private AuthnomalyDatabaseContext _context;
    private IUsersRepo _usersRepository;
    public RepositoryTests(ITestOutputHelper output)
    {
        _output = output;
        var password = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD") ?? throw new InvalidOperationException("AUTHNOMALY_DB_PASSWORD environment variable is not set.");
        var collection = new ServiceCollection();
        collection.AddDbContext<AuthnomalyDatabaseContext>(o =>
            o.UseNpgsql($"Host=localhost;Database=authnomalytest;Username=postgres;Password={password}"));
        _provider = collection.BuildServiceProvider();
        // one long-lived scope for the sequential tests; concurrency tests create their own scope per operation
        _scope = _provider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<AuthnomalyDatabaseContext>();
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
        {
            Assert.True((await _usersRepository.FindById(wrongId)).Id == 0);
            Assert.False(await _usersRepository.Delete(wrongId));
        }
    }

    [Theory]
    [InlineData(50)]
    public async Task UsersRepositoryConcurencyTests(int additionThreads)
    {
        int added = additionThreads;
        try
        {
            var addUser = async () =>
            {
                try
                {
                    using var scope = _provider.CreateScope();
                    var ctx = scope.ServiceProvider.GetService<AuthnomalyDatabaseContext>();
                    IUsersRepo repo = new UsersRepository(ctx);
                    if ((await repo.Add(new User(123133, "", ""))) == 0)
                    {
                        Interlocked.Decrement(ref added);
                    }
                }
                catch (DbUpdateException)
                {
                    Interlocked.Decrement(ref added); 
                }
            };
            var additionTasks = Enumerable.Repeat(addUser, additionThreads)
                .Select(u => u.Invoke());
            await Task.WhenAll(additionTasks);
            _output.WriteLine(added.ToString());
            Assert.True(added == 1);
            _context.ChangeTracker.Clear();
            Assert.True(await _usersRepository.Delete(123133));
            Assert.True(await _usersRepository.Add(new User(123133, "", "")) != 0);
            var deleted = 0;
            var deleteUser = async () =>
            {
                try
                {
                    using var scope = _provider.CreateScope();
                    var ctx = scope.ServiceProvider.GetService<AuthnomalyDatabaseContext>();
                    IUsersRepo repo = new UsersRepository(ctx);
                    if (await repo.Delete(123133))
                    {
                        Interlocked.Increment(ref deleted);
                    }
                }
                catch (DbUpdateException)
                {
                    return;
                }
            };
            var deletionTasks = Enumerable.Repeat(deleteUser, additionThreads)
                .Select(u => u.Invoke());
            await Task.WhenAll(deletionTasks);
            Assert.True(deleted == 1);
            Assert.True((await _usersRepository.FindById(123133)).Id == 0);
            Assert.False(await _usersRepository.Delete(123133));
            _context.ChangeTracker.Clear();
            Assert.True(await _usersRepository.Add(new User(123133, "ew", "")) != 0);
            _context.ChangeTracker.Clear();
            Assert.True((await _usersRepository.Add(new User(123133, "321", ""))) == 0);
        }
        finally
        {
            await _usersRepository.Delete(123133);
        }
}
    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}