using System.Text.Json;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Lets a plugin tell the host its declared catalogue for one capability kind has gone stale - the
/// producer side of <c>state.update</c> the SDK was missing (issue #413's remote weather-location bug):
/// without it, nothing a plugin does after its own <c>InitializeAsync</c> - reading a config entry,
/// noticing a device came or went - ever reaches the host's <c>RemotePluginCapabilitySnapshot</c> before
/// the next reconnect.
///
/// <para>
/// Resolve this from DI - the SDK registers it as a singleton for every plugin - and call
/// <see cref="IPluginCatalogNotifier.CatalogChanged" /> right
/// after whatever changed the answer a later <c>describe</c>/<c>instances</c> call would give.
/// </para>
/// </summary>
public interface IPluginCatalogNotifier
{
	/// <summary>
	/// Sends <c>state.update</c> for <paramref name="kind" /> - one of
	/// <see cref="MacroDeck.Plugin.Protocol.Handshake.CapabilityKinds" />. Fire-and-forget by contract,
	/// like <see cref="Events.IEventPublisher.Publish" />: never throws into the caller, and calling it
	/// with no open session (before connect, mid-reconnect, after shutdown) is silently a no-op rather
	/// than something the caller has to guard against.
	/// </summary>
	/// <param name="kind">The capability kind whose declared catalogue changed.</param>
	/// <param name="localId">The declared local id that went stale, when only one did. Omitted, the
	/// host re-describes the whole kind.</param>
	/// <param name="reason">Diagnostic only - the host never branches on it, see
	/// <see cref="StateUpdatePayload.Reason" />.</param>
	void CatalogChanged(string kind, string? localId = null, string? reason = null);
}

/// <summary>Sends <see cref="MessageTypes.StateUpdate" /> over whichever connection is current, mirroring
/// <see cref="RemoteEventPublisher" />'s fire-and-forget shape exactly - the two are siblings on the wire
/// (a message type of their own rather than a <c>host.invoke</c> round trip) and on the failure contract
/// (never throws, logs and drops when there is nothing to send through).</summary>
internal sealed class PluginCatalogNotifier(PluginConnectionState state, ILogger logger)
	: IPluginCatalogNotifier
{
	private readonly ILogger _logger = logger.ForContext<PluginCatalogNotifier>();

	public void CatalogChanged(string kind, string? localId = null, string? reason = null)
	{
		if (string.IsNullOrWhiteSpace(kind))
		{
			return;
		}

		var connection = state.ActiveConnection;
		if (connection is null)
		{
			return;
		}

		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.StateUpdate,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(
				new StateUpdatePayload { Kind = kind, LocalId = localId, Reason = reason },
				PluginProtocolJson.Options)
		};

		_ = SendBestEffortAsync(connection, envelope, kind);
	}

	private async Task SendBestEffortAsync(PluginSessionConnection connection, ProtocolEnvelope envelope, string kind)
	{
		try
		{
			await connection.SendAsync(envelope, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.CatalogChangeNotifyFailed(kind, exception);
		}
	}
}
