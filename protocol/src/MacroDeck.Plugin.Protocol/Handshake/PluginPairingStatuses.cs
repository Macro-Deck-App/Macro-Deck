namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>The four states a pairing request moves through.</summary>
public static class PluginPairingStatuses
{
	public const string Pending = "pending";

	public const string Approved = "approved";

	public const string Rejected = "rejected";

	public const string Expired = "expired";

	public static readonly IReadOnlyList<string> All =
	[
		Pending, Approved, Rejected, Expired,
	];
}
