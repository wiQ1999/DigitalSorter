namespace DigitalSorter.Auth;

/// <summary>Persistence boundary for broker credentials, sessions and device pairing.</summary>
public interface IBrokerStore
{
    Task<GoogleAccount?> FindAccountAsync(string subject, CancellationToken ct);
    Task SaveAccountAsync(GoogleAccount account, CancellationToken ct);
    Task<bool> HasAuthorizedAccountAsync(string? subject, string clientId, CancellationToken ct);
    Task RequireAuthorizationAsync(string subject, CancellationToken ct);
    Task UpdateRefreshCipherAsync(string subject, string cipher, CancellationToken ct);
    /// <summary>Removes the account and all sessions and pairings associated with its subject.</summary>
    Task DeleteAccountAsync(string subject, CancellationToken ct);

    Task<bool> HasWebSessionAsync(string? id, DateTimeOffset now, CancellationToken ct);
    Task<BrokerSession?> FindDeviceSessionAsync(string secretHash, DateTimeOffset now, CancellationToken ct);
    Task AddSessionAsync(BrokerSession session, CancellationToken ct);
    Task DeleteSessionAsync(string? id, CancellationToken ct);

    /// <summary>Removes expired pairings and sessions before persisting the new pairing.</summary>
    Task AddPairingAsync(Pairing pairing, DateTimeOffset now, CancellationToken ct);
    Task<Pairing?> FindPairingAsync(string id, CancellationToken ct);
    Task<Pairing?> FindPendingPairingAsync(string codeHash, DateTimeOffset now, CancellationToken ct);
    Task<bool> ApprovePairingAsync(string codeHash, string subject, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomically consumes an approved pairing and persists its session. A failed insert must roll
    /// back the claim; concurrent calls may create at most one session for the pairing.
    /// </summary>
    Task<bool> TryConsumePairingAsync(string id, BrokerSession session, CancellationToken ct);
}

/// <summary>Schema initialization belongs to the selected persistence implementation.</summary>
public interface IBrokerStoreInitializer
{
    Task InitializeAsync(CancellationToken ct = default);
}
