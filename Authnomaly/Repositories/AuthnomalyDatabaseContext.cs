using Authnomaly.Domain;
using Microsoft.EntityFrameworkCore;

namespace Authnomaly.Repositories;

public class AuthnomalyDatabaseContext : DbContext
{
    public DbSet<User> users { get; set; }
    public DbSet<AuthCredentials> authCredentials { get; set; }
    /*to be added for the next entities*/
    public AuthnomalyDatabaseContext(DbContextOptions<AuthnomalyDatabaseContext> options):base(options)
    {
        
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var password = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD")
            ?? throw new InvalidOperationException("AUTHNOMALY_DB_PASSWORD environment variable is not set.");
        optionsBuilder.UseNpgsql($"Host=localhost;Database=Authnomaly;Username=postgres;Password={password}");
        base.OnConfiguring(optionsBuilder);
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthCredentials>()
            .HasOne(a => a.Owner)
            .WithOne()
            .HasForeignKey<AuthCredentials>(a => a.Id);
    }
}