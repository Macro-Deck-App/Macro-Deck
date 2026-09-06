using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// What <see cref="MacroDeckLogShipper" /> needs from the connection, factored out of
/// <see cref="PluginConnectionState" />/<c>PluginSessionConnection</c> so the shipper's own tests can
/// fake this seam directly instead of driving a real handshake over an in-memory socket the way the
/// Hosting package's own tests do - those two types are internal to that package and this package's
/// tests have no reason to reach into them.
/// </summary>
internal interface IMacroDeckLogTransport
{
	/// <summary>Whether there is a session to send through right now.</summary>
	bool IsConnected { get; }

	/// <summary>
	/// Sends one envelope. Throws when the connection dropped between the <see cref="IsConnected" />
	/// check and this call - the shipper treats that exactly like any other send failure.
	/// </summary>
	Task SendAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>The production <see cref="IMacroDeckLogTransport" />: a thin wrapper over whichever
/// connection <see cref="PluginConnectionState" /> currently holds.</summary>
internal sealed class PluginConnectionLogTransport(PluginConnectionState state) : IMacroDeckLogTransport
{
	public bool IsConnected => state.ActiveConnection is not null;

	public async Task SendAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		var connection = state.ActiveConnection ??
			throw new InvalidOperationException("There is no active connection to send a log batch through.");

		await connection.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
	}
}
