using Authnomaly.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Authnomaly.Repositories;

public class AuthnomalyDatabaseContext : DbContext,IDataProtectionKeyContext
{
    public DbSet<User> users { get; set; }
    public DbSet<AuthCredentials> authCredentials { get; set; }
    public DbSet<LoginAttempt> loginAttempts { get; set; }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<SigningKey> SigningKeys { get; set; }
    /*to be added for the next entities*/
    public AuthnomalyDatabaseContext(DbContextOptions<AuthnomalyDatabaseContext> options):base(options)
    {
        
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var password = Environment.GetEnvironmentVariable("AUTHNOMALY_DB_PASSWORD")
                           ?? throw new InvalidOperationException(
                               "AUTHNOMALY_DB_PASSWORD environment variable is not set.");
            optionsBuilder.UseNpgsql($"Host=localhost;Database=Authnomaly;Username=postgres;Password={password}");
            base.OnConfiguring(optionsBuilder);
        }
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthCredentials>()
            .HasOne(a => a.Owner)
            .WithOne()
            .HasForeignKey<AuthCredentials>(a => a.Id);
        modelBuilder.Entity<SigningKey>()
            .HasIndex(s=>s.CreatedAt);
        modelBuilder.Entity<User>()
            .Property(u => u.Id)
            .ValueGeneratedOnAdd()
            .UseIdentityColumn();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();
    }
}