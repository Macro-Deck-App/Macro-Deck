using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Messaging;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Capabilities.Messaging;

internal sealed class MessagingCapabilityHandler(RemoteMessageChannel channel, PluginConnectionState state)
	: ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	public string Kind => CapabilityKinds.Messaging;

	// Declared unless a host said it has no message channel: an older host would otherwise mark every
	// plugin built on this SDK as partially incompatible for a capability it never asked for.
	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> state.HostMessaging == HostMessagingSupport.NotAdvertised
			? []
			: [new DeclaredCapability { Kind = CapabilityKinds.Messaging, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version }];

	public async Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (!CapabilityOperations.IsKnown(CapabilityKinds.Messaging, invocation.Operation))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The messaging capability has no operation '{invocation.Operation}'.");
		}

		MessagingDeliveryArguments? arguments;
		try
		{
			arguments = invocation.Arguments?.Deserialize<MessagingDeliveryArguments>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			arguments = null;
		}

		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload, "The message is malformed.");
		}

		return await channel.DeliverAsync(invocation.Operation, arguments, cancellationToken).ConfigureAwait(false);
	}
}
