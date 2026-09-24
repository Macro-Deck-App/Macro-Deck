namespace MacroDeckHost.Application.Licensing;

public interface IPlatformLicenseAccountClient
{
	Task<PlatformAccountLicenseResult> GetAccountLicenseAsync(long? afterRevision,
		TimeSpan wait,
		CancellationToken cancellationToken);

	Task<PlatformAccountLicenseResult> StoreAccountLicenseAsync(string license, CancellationToken cancellationToken);
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
