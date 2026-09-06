using System.Text.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// Asks a running host whether it would accept a pairing request right now, so <c>run</c> can say that
/// Developer Mode is off instead of promising a prompt that will never appear.
/// </summary>
internal interface IHostPairingProbe
{
	Task<PluginPairingDescriptor?> ProbeAsync(string hostUrl, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the host's unauthenticated protocol discovery endpoint. Every failure - unreachable host, a
/// host too old to answer, a body that is not the descriptor - answers null, because a probe that cannot
/// reach a conclusion must leave <c>run</c> reporting exactly what it reported before this probe existed.
/// </summary>
internal sealed class HostPairingProbe : IHostPairingProbe
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

	public async Task<PluginPairingDescriptor?> ProbeAsync(string hostUrl, CancellationToken cancellationToken)
	{
		try
		{
			using var client = new HttpClient { Timeout = _timeout, BaseAddress = new Uri(hostUrl) };
			var json = await client.GetStringAsync(ProtocolConstants.ProtocolDiscoveryPath, cancellationToken)
				.ConfigureAwait(false);

			return JsonSerializer.Deserialize<PluginProtocolDescriptor>(json, PluginProtocolJson.Options)?.Pairing;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Ctrl-C, not a verdict about the host.
			throw;
		}
		catch (Exception)
		{
			// Includes the TaskCanceledException HttpClient raises on its own timeout, which is a host
			// that did not answer in time rather than a caller that gave up.
			return null;
		}
	}
}
