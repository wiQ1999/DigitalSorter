using System.Security.Cryptography;
using DigitalSorter.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DigitalSorter.Auth;

public sealed class PairingService(BrokerDb db, TimeProvider clock, IConfiguration config)
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
        await db.Pairings.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(ct);
        await db.Sessions.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(ct);
        db.Pairings.Add(pairing);
        await db.SaveChangesAsync(ct);
        var origin = BrokerOrigins.Pairing(config).TrimEnd('/');
        return new(pairing.Id, code[..4] + "-" + code[4..], secret, origin + "/pair?code=" + code, pairing.ExpiresAt);
    }

    public async Task<PairingDetails?> DetailsAsync(string code, CancellationToken ct)
    {
        var hash = SecretCipher.Hash(Normalize(code));
        var now = clock.GetUtcNow();
        return await db.Pairings.Where(x => x.CodeHash == hash && x.Status == "pending" && x.ExpiresAt > now)
            .Select(x => new PairingDetails(x.DeviceName, x.ExpiresAt)).SingleOrDefaultAsync(ct);
    }

    public async Task<bool> ApproveAsync(string code, string subject, CancellationToken ct)
    {
        var hash = SecretCipher.Hash(Normalize(code));
        var now = clock.GetUtcNow();
        return await db.Pairings.Where(x => x.CodeHash == hash && x.Status == "pending" && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "approved").SetProperty(x => x.Subject, subject), ct) == 1;
    }

    public async Task<PairingResult> PollAsync(PairingPoll request, CancellationToken ct)
    {
        var pairing = await db.Pairings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (pairing is null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(pairing.SecretHash), Convert.FromHexString(SecretCipher.Hash(request.DeviceSecret))))
            return new("invalid");
        if (pairing.ExpiresAt <= clock.GetUtcNow()) return new("expired");
        if (pairing.Status != "approved") return new(pairing.Status);
        // Claim exactly once; roll back the claim if creating the session fails.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Pairings.Where(x => x.Id == request.Id && x.Status == "approved")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "consumed"), ct);
        if (claimed != 1) return new("consumed");
        var token = SecretCipher.RandomSecret();
        db.Sessions.Add(new() { Id = SecretCipher.RandomSecret(), Subject = pairing.Subject!, Kind = "device",
            SecretHash = SecretCipher.Hash(token), ExpiresAt = clock.GetUtcNow().AddDays(90) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new("approved", token);
    }
}
