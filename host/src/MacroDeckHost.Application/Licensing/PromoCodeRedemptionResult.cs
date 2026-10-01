namespace MacroDeckHost.Application.Licensing;

public static class PromoCodeRedemptionStatus
{
	public const string Redeemed = "redeemed";
	public const string AccountLicenseExists = "accountLicenseExists";
	public const string AlreadyLicensed = "alreadyLicensed";
	public const string Invalid = "invalid";
	public const string Expired = "expired";
	public const string AlreadyRedeemed = "alreadyRedeemed";
	public const string Revoked = "revoked";
	public const string AccountSuspended = "accountSuspended";
	public const string RateLimited = "rateLimited";
	public const string Unavailable = "unavailable";
	public const string SignedOut = "signedOut";
}

public sealed record PromoCodeRedemptionResult(string Status, int? RetryAfterSeconds = null);
