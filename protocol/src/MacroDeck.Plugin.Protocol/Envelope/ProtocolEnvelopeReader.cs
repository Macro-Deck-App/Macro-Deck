using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>
/// Tolerant parsing of an inbound envelope. Never throws for malformed or hostile input
/// (<see cref="OutOfMemoryException" /> excepted - that is never swallowed): a malformed body, a
/// missing required field, oversize input and JSON depth violations all become
/// <see cref="ProtocolErrorCodes.MalformedEnvelope" />. A well-formed envelope whose <c>type</c> is not
/// in <see cref="MessageTypes.IsKnown" /> becomes <see cref="ProtocolErrorCodes.UnknownMessageType" />,
/// with the parsed envelope - and so its <c>id</c> - preserved. Payload *shape* errors are not this
/// reader's business; those are <see cref="ProtocolErrorCodes.InvalidPayload" /> from the capability
/// layer in #107.
/// </summary>
public static class ProtocolEnvelopeReader
{
	public static ProtocolReadResult Read(ReadOnlySpan<byte> utf8)
	{
		// Reject oversize input before parsing, not after - parsing a hostile multi-gigabyte body is
		// itself the risk this guards against.
		if (utf8.IsEmpty || utf8.Length > ProtocolLimits.MaxMessageBytes)
		{
			return ProtocolReadResult.Failure(MalformedEnvelope());
		}

		ProtocolEnvelope? envelope;
		try
		{
			envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(utf8, PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return ProtocolReadResult.Failure(MalformedEnvelope());
		}

		if (envelope is null)
		{
			return ProtocolReadResult.Failure(MalformedEnvelope());
		}

		if (!MessageTypes.IsKnown(envelope.Type))
		{
			return ProtocolReadResult.Failure(UnknownMessageType(), envelope);
		}

		return ProtocolReadResult.Success(envelope);
	}

	private static ProtocolError MalformedEnvelope() => new()
	{
		Code = ProtocolErrorCodes.MalformedEnvelope,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.MalformedEnvelope),
		Retryable = false,
	};

	private static ProtocolError UnknownMessageType() => new()
	{
		Code = ProtocolErrorCodes.UnknownMessageType,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.UnknownMessageType),
		Retryable = false,
	};
}
