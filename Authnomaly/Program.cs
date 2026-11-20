using Authnomaly.Middlewares.RateLimiting;
using Authnomaly.Repositories;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Repositories.MemoryRepositories;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StackExchange.Redis;

public class MainClass
{
    public static void Main(string[] args)
    {
        DotNetEnv.Env.NoClobber().TraversePath().Load(); 
        string redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? throw new ArgumentException("NO REDIS HOST SET");
        string databaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? throw new ArgumentException("NO DATABASE HOST SET"); 
        string databaseName = Environment.GetEnvironmentVariable("DB_MAIN_NAME") ?? throw new ArgumentException("NO DATABASE NAME SET");
        string databaseUsername = Environment.GetEnvironmentVariable("DB_LOGIN_USERNAME") ?? throw new ArgumentException("NO DATABASE LOGIN USERNAME SET");
        string databasePassword = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD") ?? throw new ArgumentException("NO DATABASE PASSWORD SET");
        try
    {
        DbContextOptionsBuilder<AuthnomalyDatabaseContext> options = new DbContextOptionsBuilder<AuthnomalyDatabaseContext>();
        using (var db = new AuthnomalyDatabaseContext(options.Options))
        {
            db.Database.Migrate();
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Migrations error: {e.Message}");
    }
    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddDbContext<AuthnomalyDatabaseContext>(options => options.UseNpgsql($"Host={databaseHost};Database={databaseName};Username={databaseUsername};Password={databasePassword}"));
    builder.Services.AddScoped<ISigningKeyStore,SigningKeysRepository>();
    builder.Services.AddScoped<ISigningKeyProtection, DataProtectionAPIService>();
    builder.Logging.AddLog4Net("log4net.config");
    builder.Services.AddScoped<DataProtectionAPIService>();
    builder.Services.AddScoped<IUsersRepo, UsersRepository>();
    builder.Services.AddScoped<ICredentialsRepo, CredentialsRepository>();
    builder.Services.AddScoped<ILoginAttemptsRepo, LoginAttemptsRepository>();
    builder.Services.AddScoped<ILocationDetector,LocationDetectionGepIp>();
    builder.Services.AddScoped<DeviceDetails>();
    builder.Services.AddScoped<ITokenBlacklistStore, TokenBlacklistRepository>();
    builder.Services.AddScoped<IRateLimitKeysRepo,EndpointLimitDataRepository>();
    builder.Services.AddSingleton<IReadOnlyLimitKeysRepo,RatelimitReadonlyRepository>();
    builder.Services.AddSingleton<AuthnomalyDatabaseContextFactory>();
    builder.Services.AddScoped<AuthService>();
    var redis = ConnectionMultiplexer.Connect($"{redisHost},abortConnect=false");
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddStackExchangeRedisCache(options => 
        options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis));
    builder.Services.AddScoped<IRateLimitter,FixedWindowRateLimitter>();
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<JwtService>();
    builder.Services.AddControllers();
    builder.Services.AddHostedService<KeyPairRotationService>();
    builder.Services.AddHostedService<CleanupSecurityKeysService>();
    builder.Services.AddDataProtection().PersistKeysToDbContext<AuthnomalyDatabaseContext>()
                                        .SetApplicationName("Authnomaly");
    var app = builder.Build();
    app.MapControllers();
    app.UseMiddleware<RatelimitFilter>();
    app.Run(); 
    }
}
public class AuthnomalyDatabaseContextFactory : IDesignTimeDbContextFactory<AuthnomalyDatabaseContext>
{
    public AuthnomalyDatabaseContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthnomalyDatabaseContext>();
        return new AuthnomalyDatabaseContext(options.Options);
    }
}