using Microsoft.EntityFrameworkCore;

namespace DigitalSorter.Auth;

public sealed class EfBrokerStore(BrokerDb db) : IBrokerStore
{
    public Task<GoogleAccount?> FindAccountAsync(string subject, CancellationToken ct) =>
        db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Subject == subject, ct);

    public async Task SaveAccountAsync(GoogleAccount account, CancellationToken ct)
    {
        var existing = await db.Accounts.FindAsync([account.Subject], ct);
        if (existing is null) db.Accounts.Add(account);
        else db.Entry(existing).CurrentValues.SetValues(account);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> HasAuthorizedAccountAsync(string? subject, string clientId, CancellationToken ct) =>
        db.Accounts.AnyAsync(x => x.Subject == subject && !x.NeedsAuthorization && x.ClientId == clientId, ct);

    public async Task RequireAuthorizationAsync(string subject, CancellationToken ct) =>
        await db.Accounts.Where(x => x.Subject == subject)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NeedsAuthorization, true), ct);

    public async Task UpdateRefreshCipherAsync(string subject, string cipher, CancellationToken ct) =>
        await db.Accounts.Where(x => x.Subject == subject)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RefreshCipher, cipher), ct);

    public async Task DeleteAccountAsync(string subject, CancellationToken ct)
    {
        await db.Accounts.Where(x => x.Subject == subject).ExecuteDeleteAsync(ct);
        await db.Sessions.Where(x => x.Subject == subject).ExecuteDeleteAsync(ct);
        await db.Pairings.Where(x => x.Subject == subject).ExecuteDeleteAsync(ct);
    }

    public Task<bool> HasWebSessionAsync(string? id, DateTimeOffset now, CancellationToken ct) =>
        db.Sessions.AnyAsync(x => x.Id == id && x.Kind == "web" && x.ExpiresAt > now, ct);

    public Task<BrokerSession?> FindDeviceSessionAsync(string secretHash, DateTimeOffset now, CancellationToken ct) =>
        db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.SecretHash == secretHash && x.Kind == "device" && x.ExpiresAt > now, ct);

    public async Task AddSessionAsync(BrokerSession session, CancellationToken ct)
    {
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSessionAsync(string? id, CancellationToken ct) =>
        await db.Sessions.Where(x => x.Id == id).ExecuteDeleteAsync(ct);

    public async Task AddPairingAsync(Pairing pairing, DateTimeOffset now, CancellationToken ct)
    {
        await db.Pairings.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(ct);
        await db.Sessions.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(ct);
        db.Pairings.Add(pairing);
        await db.SaveChangesAsync(ct);
    }

    public Task<Pairing?> FindPairingAsync(string id, CancellationToken ct) =>
        db.Pairings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<Pairing?> FindPendingPairingAsync(string codeHash, DateTimeOffset now, CancellationToken ct) =>
        db.Pairings.AsNoTracking().SingleOrDefaultAsync(x => x.CodeHash == codeHash && x.Status == "pending" && x.ExpiresAt > now, ct);

    public async Task<bool> ApprovePairingAsync(string codeHash, string subject, DateTimeOffset now, CancellationToken ct) =>
        await db.Pairings.Where(x => x.CodeHash == codeHash && x.Status == "pending" && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "approved").SetProperty(x => x.Subject, subject), ct) == 1;

    public async Task<bool> TryConsumePairingAsync(string id, BrokerSession session, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Pairings.Where(x => x.Id == id && x.Status == "approved")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "consumed"), ct);
        if (claimed != 1) return false;
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}

public sealed class EfBrokerStoreInitializer(BrokerDb db) : IBrokerStoreInitializer
{
    public async Task InitializeAsync(CancellationToken ct = default) =>
        await db.Database.EnsureCreatedAsync(ct);
}
