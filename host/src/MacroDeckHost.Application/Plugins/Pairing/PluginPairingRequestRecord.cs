using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeckHost.Application.Plugins.Pairing;

/// <summary>
/// An in-memory, never-persisted pairing request. Deliberately holds neither the plugin secret nor the
/// PKCE verifier - the challenge is the only thing derived from the verifier that is ever stored, so a
/// process dump or a database backup can never leak a credential this record does not have.
/// </summary>
public sealed class PluginPairingRequestRecord
{
	public required string RequestId { get; init; }

	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public required string CodeChallenge { get; init; }

	public PluginPairingClientInfo? Client { get; init; }

	public required DateTimeOffset CreatedAt { get; init; }

	public required DateTimeOffset ExpiresAt { get; init; }

	public required PluginPairingRequestState State { get; set; }

	public required bool ReplaceExistingRegistration { get; set; }

	public required bool ArrivedOnPublicListener { get; init; }
}
