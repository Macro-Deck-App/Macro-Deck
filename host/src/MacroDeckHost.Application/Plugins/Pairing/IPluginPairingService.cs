using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeckHost.Application.Plugins.Pairing;

public enum PluginPairingCreateError
{
	DeveloperModeDisabled,

	InvalidPayload,

	AlreadyRegistered,

	DuplicateRequest,

	CapacityExceeded
}

public sealed record PluginPairingCreateOutcome
{
	public required bool Succeeded { get; init; }

	public PluginPairingCreateError? Error { get; init; }

	public string? ErrorDetail { get; init; }

	public PluginPairingRequestRecord? Record { get; init; }

	public static PluginPairingCreateOutcome Success(PluginPairingRequestRecord record)
		=> new() { Succeeded = true, Record = record };

	public static PluginPairingCreateOutcome Fail(PluginPairingCreateError error, string? detail = null)
		=> new() { Succeeded = false, Error = error, ErrorDetail = detail };
}

public sealed record PluginPairingStatusOutcome(string Status, DateTimeOffset ExpiresAt);

public sealed record PluginPairingPendingItem(
	string RequestId,
	string PluginId,
	string DisplayName,
	PluginPairingClientInfo? Client,
	DateTimeOffset CreatedAt,
	DateTimeOffset ExpiresAt,
	bool ReplacesExistingRegistration,
	string? ExistingRegistrationOrigin,
	DateTime? ExistingRegistrationCreatedAt,
	bool ArrivedOnPublicListener);

public enum PluginPairingApproveError
{
	NotFound,

	ReplacementNotConfirmed
}

public sealed record PluginPairingApproveOutcome
{
	public required bool Succeeded { get; init; }

	public PluginPairingApproveError? Error { get; init; }

	public static readonly PluginPairingApproveOutcome Success = new() { Succeeded = true };

	public static PluginPairingApproveOutcome Fail(PluginPairingApproveError error)
		=> new() { Succeeded = false, Error = error };
}

public sealed record PluginPairingRedeemOutcome
{
	public required bool Succeeded { get; init; }

	public string? PluginId { get; init; }

	public string? PluginSecret { get; init; }

	public static readonly PluginPairingRedeemOutcome Fail = new() { Succeeded = false };

	public static PluginPairingRedeemOutcome Success(string pluginId, string pluginSecret)
		=> new() { Succeeded = true, PluginId = pluginId, PluginSecret = pluginSecret };
}

public sealed record PluginPairedRegistration(
	string PluginId,
	string DisplayName,
	DateTime CreatedAt,
	DateTime? LastSeenAt,
	bool Online);

public interface IPluginPairingService
{
	Task<PluginPairingCreateOutcome> Create(string pluginId,
		string displayName,
		string codeChallenge,
		string codeChallengeMethod,
		PluginPairingClientInfo? client,
		bool arrivedOnPublicListener);

	PluginPairingStatusOutcome Status(string requestId);

	Task<IReadOnlyList<PluginPairingPendingItem>> Pending();

	Task<PluginPairingApproveOutcome> Approve(string requestId, bool replaceExistingRegistration);

	bool Reject(string requestId);

	Task<PluginPairingRedeemOutcome> Redeem(string requestId, string codeVerifier);

	Task<IReadOnlyList<PluginPairedRegistration>> PairedRegistrations();
}
