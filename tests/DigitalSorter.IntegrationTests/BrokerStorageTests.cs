using DigitalSorter.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalSorter.IntegrationTests;

public sealed class BrokerStorageTests
{
    [Fact]
    public async Task ExistingSqliteSchemaAndUtcTicksRemainReadableAndWritable()
    {
        using var factory = new BrokerFactory();
        Directory.CreateDirectory(factory.DataDirectory);
        var connectionString = new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(factory.DataDirectory, "broker.db"), Pooling = false }.ToString();
        // Schema from the original broker, independent of the current EF model.
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE Accounts (Subject TEXT NOT NULL PRIMARY KEY, ClientId TEXT NOT NULL,
                Name TEXT NOT NULL, RefreshCipher TEXT NOT NULL, Scopes TEXT NOT NULL, NeedsAuthorization INTEGER NOT NULL);
            CREATE TABLE Sessions (Id TEXT NOT NULL PRIMARY KEY, Subject TEXT NOT NULL,
                SecretHash TEXT NULL, Kind TEXT NOT NULL, ExpiresAt INTEGER NOT NULL);
            CREATE UNIQUE INDEX IX_Sessions_SecretHash ON Sessions (SecretHash);
            CREATE INDEX IX_Sessions_Subject ON Sessions (Subject);
            CREATE TABLE Pairings (Id TEXT NOT NULL PRIMARY KEY, CodeHash TEXT NOT NULL,
                SecretHash TEXT NOT NULL, DeviceName TEXT NOT NULL, Status TEXT NOT NULL,
                Subject TEXT NULL, ExpiresAt INTEGER NOT NULL);
            CREATE UNIQUE INDEX IX_Pairings_CodeHash ON Pairings (CodeHash);
            INSERT INTO Accounts VALUES ('legacy-user', 'legacy-client', 'Legacy', 'encrypted', 'openid', 0);
            INSERT INTO Sessions VALUES ('legacy-web', 'legacy-user', NULL, 'web', $expires);
            INSERT INTO Pairings VALUES ('legacy-pair', 'legacy-code', 'legacy-secret', 'TV', 'pending', NULL, $expires);
            """;
        var expires = factory.Clock.GetUtcNow().AddMinutes(5);
        create.Parameters.AddWithValue("$expires", expires.UtcTicks);
        await create.ExecuteNonQueryAsync();

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBrokerStore>();
        var account = (await store.FindAccountAsync("legacy-user", default))!;
        Assert.Equal("Legacy", account.Name);
        Assert.True(await store.HasWebSessionAsync("legacy-web", factory.Clock.GetUtcNow(), default));
        Assert.Equal(expires, (await store.FindPendingPairingAsync("legacy-code", factory.Clock.GetUtcNow(), default))!.ExpiresAt);
        account.Name = "Updated";
        await store.SaveAccountAsync(account, default);
        Assert.Equal("Updated", (await store.FindAccountAsync("legacy-user", default))!.Name);
        Assert.True(await store.ApprovePairingAsync("legacy-code", "legacy-user", factory.Clock.GetUtcNow(), default));
    }

    [Fact]
    public async Task FailedSessionInsertRollsBackPairingClaimAndAllowsRetry()
    {
        using var factory = new BrokerFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IBrokerStore>();
            await store.AddPairingAsync(new Pairing { Id = "pair", CodeHash = "code", Status = "approved",
                Subject = "user", ExpiresAt = factory.Clock.GetUtcNow().AddMinutes(5) }, factory.Clock.GetUtcNow(), default);
            await store.AddSessionAsync(DeviceSession("existing", "duplicate"), default);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IBrokerStore>();
            await Assert.ThrowsAsync<DbUpdateException>(() => store.TryConsumePairingAsync("pair", DeviceSession("failed", "duplicate"), default));
        }
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IBrokerStore>();
            Assert.Equal("approved", (await store.FindPairingAsync("pair", default))!.Status);
            Assert.True(await store.TryConsumePairingAsync("pair", DeviceSession("retry", "unique"), default));
            Assert.False(await store.TryConsumePairingAsync("pair", DeviceSession("replay", "replayed"), default));
            Assert.NotNull(await store.FindDeviceSessionAsync("unique", factory.Clock.GetUtcNow(), default));
            Assert.Null(await store.FindDeviceSessionAsync("replayed", factory.Clock.GetUtcNow(), default));
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<BrokerDb>().Sessions.CountAsync());
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task SessionAndPairingExpiryUseTheSameUtcBoundary(int seconds, bool valid)
    {
        using var factory = new BrokerFactory();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBrokerStore>();
        var now = factory.Clock.GetUtcNow();
        var expires = now.AddSeconds(seconds).ToOffset(TimeSpan.FromHours(2));
        await store.AddSessionAsync(new BrokerSession { Id = "web", ExpiresAt = expires }, default);
        await store.AddSessionAsync(new BrokerSession { Id = "device", Kind = "device", SecretHash = "hash", ExpiresAt = expires }, default);
        await store.AddPairingAsync(new Pairing { Id = "pair", CodeHash = "code", ExpiresAt = expires }, now.AddDays(-1), default);
        Assert.Equal(valid, await store.HasWebSessionAsync("web", now, default));
        Assert.Equal(valid, await store.FindDeviceSessionAsync("hash", now, default) is not null);
        Assert.Equal(valid, await store.FindPendingPairingAsync("code", now, default) is not null);
        Assert.Equal(valid, await store.ApprovePairingAsync("code", "user", now, default));
    }

    [Fact]
    public async Task SqliteConnectionStringCanBeConfiguredWithoutChangingTheDefaultDirectory()
    {
        using var connection = new SqliteConnection($"Data Source=broker-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Broker"] = connection.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddSqliteBrokerStorage(configuration, "unused-directory");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBrokerStoreInitializer>().InitializeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(connection.ConnectionString, db.Database.GetConnectionString());
        await scope.ServiceProvider.GetRequiredService<IBrokerStore>().SaveAccountAsync(new GoogleAccount { Subject = "configured" }, default);
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Accounts";
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    private static BrokerSession DeviceSession(string id, string hash) => new()
    {
        Id = id, Subject = "user", Kind = "device", SecretHash = hash, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };
}
