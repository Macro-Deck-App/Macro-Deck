using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeckHost.Application.Plugins.Pairing;

public enum PluginPairingCreateFailure
{
	DuplicateRequest,

	CapacityExceeded
}

public sealed record PluginPairingCreateResult
{
	public required bool Succeeded { get; init; }

	public PluginPairingCreateFailure? Failure { get; init; }

	public PluginPairingRequestRecord? Record { get; init; }

	public static PluginPairingCreateResult Success(PluginPairingRequestRecord record)
		=> new() { Succeeded = true, Record = record };

	public static PluginPairingCreateResult Fail(PluginPairingCreateFailure failure)
		=> new() { Succeeded = false, Failure = failure };
}

public enum PluginPairingRedeemFailure
{
	NotFound,

	NotApproved,

	VerificationFailed
}

public sealed record PluginPairingRedeemResult
{
	public required bool Succeeded { get; init; }

	public PluginPairingRedeemFailure? Failure { get; init; }

	public PluginPairingRequestRecord? Record { get; init; }

	public static PluginPairingRedeemResult Success(PluginPairingRequestRecord record)
		=> new() { Succeeded = true, Record = record };

	public static PluginPairingRedeemResult Fail(PluginPairingRedeemFailure failure)
		=> new() { Succeeded = false, Failure = failure };
}

/// <summary>
/// The live set of interactive pairing requests. Singleton, in-memory, never persisted - a pairing
/// request is a short-lived, host-restart-safe-to-lose prompt, not durable state.
/// </summary>
public interface IPluginPairingRequestStore
{
	PluginPairingCreateResult Create(string pluginId,
		string displayName,
		string codeChallenge,
		PluginPairingClientInfo? client,
		bool arrivedOnPublicListener);

	PluginPairingRequestRecord? Find(string requestId);

	/// <summary>All non-expired records, for building UI/status projections.</summary>
	IReadOnlyList<PluginPairingRequestRecord> Snapshot();

	bool Approve(string requestId, bool replaceExistingRegistration);

	bool Reject(string requestId);

	PluginPairingRedeemResult TryRedeem(string requestId, string codeVerifier);

	/// <summary>Finalises a request left in <see cref="PluginPairingRequestState.Redeeming" /> by
	/// <see cref="TryRedeem" />: removes it on success, or marks it terminally
	/// <see cref="PluginPairingRequestState.Failed" /> on failure. Never call this without a prior
	/// successful <see cref="TryRedeem" /> for the same id.</summary>
	void CompleteRedemption(string requestId, bool succeeded);
}
