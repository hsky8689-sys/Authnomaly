using Authnomaly.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Authnomaly.Tests;

// All classes that touch authnomalytest share this collection so they never run in parallel with each other
[CollectionDefinition("Database")]
public class DatabaseCollection { }

public sealed class TestScope : IDisposable
{
    private readonly IServiceScope _scope;
    public AuthnomalyDatabaseContext Context { get; }
    public TestScope(IServiceScope scope)
    {
        _scope = scope;
        Context = scope.ServiceProvider.GetRequiredService<AuthnomalyDatabaseContext>();
    }
    public void Dispose() => _scope.Dispose();
}

// Same registration the app uses (Scoped DbContext), pointed at the test database
public sealed class TestDb : IDisposable
{
    private static readonly object MigrateLock = new();
    private static bool _migrated;
    private readonly ServiceProvider _provider;
    private readonly IConnectionMultiplexer _multiplexer;
    public TestDb()
    {
        var password = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD")
                       ?? throw new InvalidOperationException("AUTHNOMALY_DB_PASSWORD environment variable is not set.");
        var services = new ServiceCollection();
        services.AddDbContext<AuthnomalyDatabaseContext>(o =>
            o.UseNpgsql($"Host=localhost;Database=authnomalytest;Username=postgres;Password={password}"));
        _provider = services.BuildServiceProvider();
        var redis = ConnectionMultiplexer.Connect("localhost");
        _multiplexer=redis;
        services.AddStackExchangeRedisCache(options =>
            options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis));
        // keep the test schema in sync with the migrations (the app only migrates the dev database on startup)
        lock (MigrateLock)
        {
            if (_migrated) return;
            using var scope = _provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AuthnomalyDatabaseContext>().Database.Migrate();
            _migrated = true;
        }
    }
    public IDatabase NewRedis() => _multiplexer.GetDatabase();
    public TestScope NewScope() => new(_provider.CreateScope());
    public void Dispose() => _provider.Dispose();
}
