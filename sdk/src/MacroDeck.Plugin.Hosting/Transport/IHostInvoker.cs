using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// Sends <c>host.invoke</c> to the host and awaits its <c>host.result</c>. The plugin-side mirror of
/// the host's <c>IPluginCapabilityInvoker</c>: the same exchange, run from the opposite end.
/// </summary>
internal interface IHostInvoker
{
	/// <summary>Sends <c>host.invoke</c> for one <c>HostApis</c>/<c>HostOperations</c> pair and awaits
	/// the reply. Throws <see cref="HostInvocationException"/> when the host answers with an error, and
	/// fails fast (no buffering) when there is currently no connection - matching the host's own
	/// <c>SendToPlugin</c> behaviour for the opposite direction. A connection that ends with the call
	/// still in flight fails it the same way, retryably, rather than leaving it to wait out the request
	/// timeout.</summary>
	Task<JsonElement?> InvokeAsync(string api,
		string operation,
		object? arguments,
		CancellationToken cancellationToken);

	/// <summary>Completes a pending invocation from the connection's receive loop. False when the
	/// correlation is unknown to this invoker.</summary>
	bool TryComplete(ProtocolEnvelope result);
}
