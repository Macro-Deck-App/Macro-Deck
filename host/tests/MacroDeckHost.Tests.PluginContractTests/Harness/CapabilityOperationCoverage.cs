using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal static class CapabilityOperationCoverage
{
	private static readonly Lock _gate = new();
	private static readonly Dictionary<string, HashSet<string>> _byKind = new(StringComparer.Ordinal);

	public static void RecordFrom(IReadOnlyList<ProtocolEnvelope> sentByHost)
	{
		foreach (var envelope in sentByHost)
		{
			if (!string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal))
			{
				continue;
			}

			var payload = envelope.Payload?.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options);
			if (payload is null)
			{
				continue;
			}

			lock (_gate)
			{
				if (!_byKind.TryGetValue(payload.Kind, out var operations))
				{
					operations = new HashSet<string>(StringComparer.Ordinal);
					_byKind[payload.Kind] = operations;
				}

				operations.Add(payload.Operation);
			}
		}
	}

	public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Snapshot()
	{
		lock (_gate)
		{
			return _byKind.ToDictionary(pair => pair.Key,
				pair => (IReadOnlyCollection<string>)[.. pair.Value],
				StringComparer.Ordinal);
		}
	}
}
