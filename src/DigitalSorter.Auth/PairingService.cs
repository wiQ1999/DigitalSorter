using System.Security.Cryptography;
using DigitalSorter.Contracts;

namespace DigitalSorter.Auth;

public sealed class PairingService(IBrokerStore store, TimeProvider clock, IConfiguration config)
{
    public static string Normalize(string code) => new(code.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
    public async Task<PairingStarted> StartAsync(string name, CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = string.Concat(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
        var secret = SecretCipher.RandomSecret();
        var pairing = new Pairing
        {
            Id = SecretCipher.RandomSecret(), CodeHash = SecretCipher.Hash(code), SecretHash = SecretCipher.Hash(secret),
            DeviceName = string.IsNullOrWhiteSpace(name) ? "Android TV" : name.Trim()[..Math.Min(name.Trim().Length, 80)],
            ExpiresAt = clock.GetUtcNow().AddMinutes(5)
        };
        var now = clock.GetUtcNow();
        await store.AddPairingAsync(pairing, now, ct);
        var origin = BrokerOrigins.Pairing(config).TrimEnd('/');
        return new(pairing.Id, code[..4] + "-" + code[4..], secret, origin + "/pair?code=" + code, pairing.ExpiresAt);
    }

    public async Task<PairingDetails?> DetailsAsync(string code, CancellationToken ct)
    {
        var hash = SecretCipher.Hash(Normalize(code));
        var now = clock.GetUtcNow();
        var pairing = await store.FindPendingPairingAsync(hash, now, ct);
        return pairing is null ? null : new PairingDetails(pairing.DeviceName, pairing.ExpiresAt);
    }

    public async Task<bool> ApproveAsync(string code, string subject, CancellationToken ct)
    {
        var hash = SecretCipher.Hash(Normalize(code));
        var now = clock.GetUtcNow();
        return await store.ApprovePairingAsync(hash, subject, now, ct);
    }

    public async Task<PairingResult> PollAsync(PairingPoll request, CancellationToken ct)
    {
        var pairing = await store.FindPairingAsync(request.Id, ct);
        if (pairing is null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(pairing.SecretHash), Convert.FromHexString(SecretCipher.Hash(request.DeviceSecret))))
            return new("invalid");
        if (pairing.ExpiresAt <= clock.GetUtcNow()) return new("expired");
        if (pairing.Status != "approved") return new(pairing.Status);
        var token = SecretCipher.RandomSecret();
        var session = new BrokerSession { Id = SecretCipher.RandomSecret(), Subject = pairing.Subject!, Kind = "device",
            SecretHash = SecretCipher.Hash(token), ExpiresAt = clock.GetUtcNow().AddDays(90) };
        return await store.TryConsumePairingAsync(request.Id, session, ct) ? new("approved", token) : new("consumed");
    }
}
