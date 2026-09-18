using Authnomaly.Repositories;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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
    Console.WriteLine($"Eroare la rulare migrari: {e.Message}");
}
var builder = WebApplication.CreateBuilder(args);
var password = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD") ?? throw new InvalidOperationException("AUTHNOMALY_DB_PASSWORD environment variable is not set.");
builder.Services.AddDbContext<AuthnomalyDatabaseContext>(options =>
    options.UseNpgsql($"Host=localhost;Database=Authnomaly;Username=postgres;Password={password}"));
builder.Services.AddScoped<ISigningKeyStore,SigningKeysRepository>();
builder.Services.AddScoped<ISigningKeyProtection, DataProtectionAPIService>();
builder.Services.AddScoped<DataProtectionAPIService>();
builder.Services.AddScoped<IUsersRepo, UsersRepository>();
builder.Services.AddScoped<ICredentialsRepo, CredentialsRepository>();
builder.Services.AddScoped<ILoginAttemptsRepo, LoginAttemptsRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddControllers();
builder.Services.AddHostedService<KeyPairRotationService>();
builder.Services.AddHostedService<CleanupSecurityKeysService>();
builder.Services.AddDataProtection().PersistKeysToDbContext<AuthnomalyDatabaseContext>();
var app = builder.Build();
app.MapControllers();
app.Run();
public class AuthnomalyDatabaseContextFactory : IDesignTimeDbContextFactory<AuthnomalyDatabaseContext>
{
    public AuthnomalyDatabaseContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthnomalyDatabaseContext>();
        return new AuthnomalyDatabaseContext(options.Options);
    }
}