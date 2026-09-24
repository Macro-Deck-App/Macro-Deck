namespace MacroDeckHost.Application.Licensing;

public class CompanionLicenseStatus
{
	public bool Licensed { get; set; }

	public string? LicenseId { get; set; }

	public string? Source { get; set; }

	public string? KeyId { get; set; }

	public long? IssuedAt { get; set; }

	public long? PurchasedAt { get; set; }

	public string? BillingId { get; set; }

	public bool IsTest { get; set; }

	public string AccountSync { get; set; } = CompanionLicenseAccountSync.Unknown;

	public bool IssuePending { get; set; }

	public long? NextIssueAttemptAt { get; set; }
}
