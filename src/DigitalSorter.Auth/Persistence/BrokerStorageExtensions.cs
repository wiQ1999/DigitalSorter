using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DigitalSorter.Auth;

public static class BrokerStorageExtensions
{
    public static IServiceCollection AddRelationalBrokerStorage(this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddDbContext<BrokerDb>(configureDatabase);
        services.AddScoped<IBrokerStore, EfBrokerStore>();
        services.AddScoped<IBrokerStoreInitializer, EfBrokerStoreInitializer>();
        return services;
    }

    public static IServiceCollection AddSqliteBrokerStorage(this IServiceCollection services,
        IConfiguration configuration, string dataDirectory)
    {
        var connectionString = configuration.GetConnectionString("Broker")
            ?? new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "broker.db") }.ToString();
        return services.AddRelationalBrokerStorage(options => options.UseSqlite(connectionString));
    }
}
