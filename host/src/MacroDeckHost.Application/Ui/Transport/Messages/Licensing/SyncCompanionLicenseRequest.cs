namespace MacroDeckHost.Application.Ui.Transport.Messages.Licensing;

public class SyncCompanionLicenseRequest
{
	public string? License { get; set; }

	public CompanionLicenseProof? Proof { get; set; }

	public string? TrialDeviceId { get; set; }

	public bool TrialStarted { get; set; }
}

public class CompanionLicenseProof
{
	public string? Platform { get; set; }

	public string? ProductId { get; set; }

	public string? PurchaseToken { get; set; }

	public string? OrderId { get; set; }

	public string? PackageName { get; set; }

	public string? TransactionId { get; set; }

	public string? SignedPayload { get; set; }

	public string? LegacyKind { get; set; }
}
