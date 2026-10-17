namespace DigitalSorter.Contracts;

public sealed record SessionInfo(bool Authenticated, string? DisplayName = null, bool Device = false, string? CsrfToken = null, bool GoogleAuthorizationRequired = false);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
public sealed record PairingRequest(string DeviceName);
public sealed record PairingStarted(string Id, string UserCode, string DeviceSecret, string VerificationUri,
    DateTimeOffset ExpiresAt, int IntervalSeconds = 3);
public sealed record PairingPoll(string Id, string DeviceSecret);
public sealed record PairingResult(string Status, string? DeviceToken = null);
public sealed record PairingConfirmation(string Code);
public sealed record PairingDetails(string DeviceName, DateTimeOffset ExpiresAt);
