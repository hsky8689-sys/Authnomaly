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
    private static readonly string RedisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? throw new ArgumentException("NO REDIS HOST SET");
    private static readonly string DatabaseHost = Environment.GetEnvironmentVariable("TEST_DB_HOST") ?? throw new ArgumentException("NO DATABASE HOST SET");
    private static readonly string DatabaseName = Environment.GetEnvironmentVariable("TEST_DB_NAME") ?? throw new ArgumentException("NO DATABASE NAME SET");
    private static readonly string DatabaseUsername = Environment.GetEnvironmentVariable("TEST_DB_USERNAME") ?? throw new ArgumentException("NO DATABASE LOGIN USERNAME SET");
    private static readonly string DatabasePassword = Environment.GetEnvironmentVariable("TEST_DB_PASSWORD") ?? throw new ArgumentException("NO DATABASE PASSWORD SET");
    private static readonly int SyncTimeout = Int32.Parse(Environment.GetEnvironmentVariable("TEST_REDIS_SYNC_TIMEOUT") ?? throw new ArgumentException("NO SYNC TIMEOUT SET"));
    private static readonly int AsyncTimeout = Int32.Parse(Environment.GetEnvironmentVariable("TEST_REDIS_ASYNC_TIMEOUT") ?? throw new ArgumentException("NO ASYNC TIMEOUT SET"));
    private static readonly int ConnectRetries = Int32.Parse(Environment.GetEnvironmentVariable("TEST_REDIS_CONNECT_RETRY") ?? throw new ArgumentException("NO CONNECT RETRY SET"));
    public TestDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuthnomalyDatabaseContext>(o =>
            o.UseNpgsql($"Host={DatabaseHost};Database={DatabaseName};Username={DatabaseUsername};Password={DatabasePassword}"));
        _provider = services.BuildServiceProvider();
        var redis = ConnectionMultiplexer.Connect($"{RedisHost},allowAdmin=true,abortConnect=false,syncTimeout={SyncTimeout},asyncTimeout={AsyncTimeout},connectRetry={ConnectRetries}");
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

    public IConnectionMultiplexer Multiplexer => _multiplexer;
    public IDatabase NewRedis() => _multiplexer.GetDatabase();
    public TestScope NewScope() => new(_provider.CreateScope());
    public void Dispose()
    {
        _provider.Dispose();
        _multiplexer.Dispose();
    }
}
