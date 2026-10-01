namespace MacroDeckHost.Application.Licensing;

public interface IPlatformLicenseAccountClient
{
	Task<PlatformAccountLicenseResult> GetAccountLicenseAsync(long? afterRevision,
		TimeSpan wait,
		CancellationToken cancellationToken);

	Task<PlatformAccountLicenseResult> StoreAccountLicenseAsync(string license, CancellationToken cancellationToken);

	Task<PlatformPromoCodeResult> RedeemPromoCodeAsync(string code, CancellationToken cancellationToken);
}

public enum PromoCodeRejection
{
	Invalid,
	Expired,
	AlreadyRedeemed,
	Revoked
}

public abstract record PlatformPromoCodeResult
{
	private PlatformPromoCodeResult()
	{
	}

	public sealed record Redeemed(string License) : PlatformPromoCodeResult;

	public sealed record AccountLicenseExists(string License) : PlatformPromoCodeResult;

	public sealed record Rejected(PromoCodeRejection Reason) : PlatformPromoCodeResult;

	public sealed record AccountSuspended : PlatformPromoCodeResult;

	public sealed record RateLimited(TimeSpan? RetryAfter) : PlatformPromoCodeResult;

	public sealed record Unavailable : PlatformPromoCodeResult;

	public sealed record SignedOut : PlatformPromoCodeResult;
}

public abstract record PlatformAccountLicenseResult
{
	private PlatformAccountLicenseResult()
	{
	}

	public sealed record Current(string? License, long Revision, TimeSpan? RetryAfter = null) : PlatformAccountLicenseResult;

	public sealed record Conflict(string? License, long Revision) : PlatformAccountLicenseResult;

	public sealed record Refused(string Code) : PlatformAccountLicenseResult;

	public sealed record Unavailable(TimeSpan? RetryAfter, bool AccountBlocked = false) : PlatformAccountLicenseResult;

	public sealed record SignedOut : PlatformAccountLicenseResult;
}
