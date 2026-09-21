using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;

namespace MacroDeckHost.Application.Licensing;

public interface IPlatformLicenseClient
{
	Task<PlatformLicenseIssueResult> IssueCompanionLicenseAsync(CompanionLicenseProof proof,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<string>?> GetRevokedLicenseIdsAsync(CancellationToken cancellationToken);
}

public abstract record PlatformLicenseIssueResult
{
	private PlatformLicenseIssueResult()
	{
	}

	public sealed record Issued(string License) : PlatformLicenseIssueResult;

	public sealed record Retry(TimeSpan? RetryAfter, bool PurchasePending) : PlatformLicenseIssueResult;

	public sealed record Refused(string Code) : PlatformLicenseIssueResult;
}
