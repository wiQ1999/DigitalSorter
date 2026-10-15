using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;

namespace DigitalSorter.Auth;

public sealed class GoogleAccount
{
    public string Subject { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "Konto Google";
    public string RefreshCipher { get; set; } = "";
    public string Scopes { get; set; } = "";
    public bool NeedsAuthorization { get; set; }
}

public sealed class BrokerSession
{
    public string Id { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? SecretHash { get; set; }
    public string Kind { get; set; } = "web";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class Pairing
{
    public string Id { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Subject { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

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
        // SQLite compares UTC ticks consistently, including expiry predicates.
        model.Entity<BrokerSession>().Property(x => x.ExpiresAt).HasConversion(x => x.UtcTicks, x => new DateTimeOffset(x, TimeSpan.Zero));
        model.Entity<Pairing>().Property(x => x.ExpiresAt).HasConversion(x => x.UtcTicks, x => new DateTimeOffset(x, TimeSpan.Zero));
    }
}

public sealed class SecretCipher(byte[] key)
{
    public string Protect(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var encrypted = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, encrypted, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. encrypted]);
    }
    public string Unprotect(string value)
    {
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length < 28) throw new CryptographicException("Invalid encrypted credential.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
    public static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class KeyRingEncryptor(SecretCipher cipher) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement) => new(
        new XElement("encryptedKey", cipher.Protect(plaintextElement.ToString(SaveOptions.DisableFormatting))), typeof(KeyRingDecryptor));
}

public sealed class KeyRingDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement) => XElement.Parse(
        services.GetRequiredService<SecretCipher>().Unprotect(encryptedElement.Value));
}
