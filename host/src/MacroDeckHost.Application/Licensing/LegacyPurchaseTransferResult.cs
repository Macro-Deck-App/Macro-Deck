namespace MacroDeckHost.Application.Licensing;

public enum LegacyPurchaseTransferStatus
{
	Transferred,
	AlreadyTransferred,
	Pending,
	Rejected,
	Unavailable
}

public sealed record LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus Status, string? Code = null);
