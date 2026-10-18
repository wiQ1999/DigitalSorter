namespace DigitalSorter.Auth;

public static class BrokerOrigins
{
    public static string Validate(string value, string setting)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException($"{setting} must be an HTTPS origin without a path.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string Pairing(IConfiguration config) => config["Broker:PairingOrigin"] ?? config["Broker:PublicOrigin"]!;
}
