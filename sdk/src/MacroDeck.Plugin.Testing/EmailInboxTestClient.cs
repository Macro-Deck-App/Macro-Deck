using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>email-inbox</c> capability. A provider may serve several inboxes; the arguments of
/// <see cref="GetSnapshotAsync" /> are where the inbox id being asked about lives, not the wire local id.
/// </summary>
public sealed class EmailInboxTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal EmailInboxTestClient(ICapabilityInvoker invoker)
	{
		_invoker = invoker;
	}

	/// <summary>
	/// Reads an <c>EmailInboxDescribePayload</c>: the first non-empty provider name of the plugin's email
	/// inbox integrations, or the manifest name when none sets one, and every inbox id once, at most
	/// <c>ProtocolLimits.MaxEmailInboxes</c>.
	/// </summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
	{
		return _invoker.InvokeAsync(CapabilityKinds.EmailInbox,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.EmailInbox.Describe,
			null,
			options);
	}

	/// <summary>
	/// Reads an <c>EmailInboxesResult</c> with the same inbox list <see cref="DescribeAsync" /> carries; an
	/// id two integrations announce is listed once, for the first of them.
	/// </summary>
	public Task<CapabilityInvocationOutcome> GetInboxesAsync(CapabilityInvokeOptions? options = null)
	{
		return _invoker.InvokeAsync(CapabilityKinds.EmailInbox,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.EmailInbox.Inboxes,
			null,
			options);
	}

	/// <summary>
	/// Reads an <c>EmailInboxSnapshotResult</c> from the integration that owns the inbox: the status as a
	/// name, the counts and at most the requested number of messages, newest first, with the field limits
	/// applied and <c>Truncated</c> set when messages were left out to fit
	/// <c>ProtocolLimits.MaxEmailInboxReplyBytes</c>. An unknown inbox fails with
	/// <c>ProtocolErrorCodes.CapabilityUnavailable</c>; a provider that throws fails with
	/// <c>ProtocolErrorCodes.InternalError</c>.
	/// </summary>
	public Task<CapabilityInvocationOutcome> GetSnapshotAsync(
		EmailInboxSnapshotArguments arguments,
		CapabilityInvokeOptions? options = null)
	{
		return _invoker.InvokeAsync(CapabilityKinds.EmailInbox,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.EmailInbox.Snapshot,
			arguments,
			options);
	}
}
