using System.Text.Json;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>Serializes an outbound envelope with the one contract-wide <see cref="JsonSerializerOptions" />.</summary>
public static class ProtocolEnvelopeWriter
{
	public static byte[] WriteToUtf8Bytes(ProtocolEnvelope envelope)
		=> JsonSerializer.SerializeToUtf8Bytes(envelope, PluginProtocolJson.Options);

	public static string WriteToString(ProtocolEnvelope envelope)
		=> JsonSerializer.Serialize(envelope, PluginProtocolJson.Options);
}
