using Microsoft.EntityFrameworkCore;

namespace DigitalSorter.Auth;

public sealed class BrokerDb(DbContextOptions<BrokerDb> options) : DbContext(options)
{
    public DbSet<GoogleAccount> Accounts => Set<GoogleAccount>();
    public DbSet<BrokerSession> Sessions => Set<BrokerSession>();
    public DbSet<Pairing> Pairings => Set<Pairing>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<GoogleAccount>().HasKey(x => x.Subject);
        model.Entity<BrokerSession>().HasKey(x => x.Id);
        model.Entity<BrokerSession>().HasIndex(x => x.SecretHash).IsUnique();
        model.Entity<BrokerSession>().HasIndex(x => x.Subject);
        model.Entity<Pairing>().HasKey(x => x.Id);
        model.Entity<Pairing>().HasIndex(x => x.CodeHash).IsUnique();
        // UTC ticks preserve the existing schema and support relational expiry comparisons.
        model.Entity<BrokerSession>().Property(x => x.ExpiresAt).HasConversion(x => x.UtcTicks, x => new DateTimeOffset(x, TimeSpan.Zero));
        model.Entity<Pairing>().Property(x => x.ExpiresAt).HasConversion(x => x.UtcTicks, x => new DateTimeOffset(x, TimeSpan.Zero));
    }
}
