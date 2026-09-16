using Authnomaly.Repositories;
using Authnomaly.Utils;
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
builder.Services.AddScoped<DbContext, AuthnomalyDatabaseContext>();
var app = builder.Build();
var pass = "0763juiu";
var data = Encryption.HashPassword(pass);
var hashedData = data.Hash;
var salt = data.Salt;
//Console.WriteLine(Convert.ToBase64String(hashedData));
Console.WriteLine(Encryption.VerifyPassword(pass,hashedData,salt));
app.Run();
public class AuthnomalyDatabaseContextFactory : IDesignTimeDbContextFactory<AuthnomalyDatabaseContext>
{
    public AuthnomalyDatabaseContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthnomalyDatabaseContext>();
        return new AuthnomalyDatabaseContext(options.Options);
    }
}