using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;

// Kept to protocol and SDK types so the host can compile this file as a linked source.

internal static class VideoStreamWire
{
	public static VideoStreamState ParseState(string? value) => Parse(value, VideoStreamState.Unavailable);

	public static VideoStreamSessionState ParseSessionState(string? value)
		=> Parse(value, VideoStreamSessionState.Reconnecting);

	public static VideoStreamSessionReason ParseReason(string? value) => Parse(value, VideoStreamSessionReason.None);

	public static VideoStreamConnectionKind ParseConnectionKind(string? value)
		=> Parse(value, VideoStreamConnectionKind.Network);

	public static VideoStreamDescriptorDto ToDto(VideoStreamDescriptor stream)
		=> new()
		{
			Id = stream.Id,
			Name = stream.Name,
			Description = stream.Description,
			Width = stream.Width,
			Height = stream.Height,
			HasAudio = stream.HasAudio,
			State = stream.State.ToString(),
			Metadata = stream.Metadata
		};

	public static VideoStreamDescriptor ToDescriptor(VideoStreamDescriptorDto dto)
		=> new(dto.Id,
			dto.Name,
			dto.Description,
			dto.Width,
			dto.Height,
			dto.HasAudio,
			ParseState(dto.State),
			dto.Metadata);

	public static VideoStreamSessionDescriptionDto ToDto(VideoStreamSessionDescription description)
		=> new()
		{
			Transport = description.Transport,
			Url = description.Url,
			Parameters = description.Parameters,
			Payload = description.Payload,
			ExpiresAt = description.ExpiresAt
		};

	public static VideoStreamSessionDescription ToDescription(VideoStreamSessionDescriptionDto dto)
		=> new(dto.Transport, dto.Url, dto.Parameters, dto.Payload, dto.ExpiresAt);

	public static VideoStreamSignalDto ToDto(VideoStreamSignal signal)
		=> new() { Type = signal.Type, Payload = signal.Payload };

	public static VideoStreamSignal ToSignal(VideoStreamSignalDto dto) => new(dto.Type, dto.Payload);

	public static VideoStreamConsumerDto ToDto(VideoStreamConsumer consumer)
		=> new()
		{
			DeviceId = consumer.DeviceId,
			HostAddress = consumer.HostAddress?.ToString(),
			ConnectionKind = consumer.ConnectionKind.ToString()
		};

	public static VideoStreamConsumer ToConsumer(VideoStreamConsumerDto? dto)
		=> dto is null
			? new VideoStreamConsumer(null, null, VideoStreamConnectionKind.Network)
			: new VideoStreamConsumer(dto.DeviceId,
				Uri.TryCreate(dto.HostAddress, UriKind.Absolute, out var address) ? address : null,
				ParseConnectionKind(dto.ConnectionKind));

	public static string? ValidateDescriptor(VideoStreamDescriptor? stream)
	{
		if (stream is null)
		{
			return "A stream descriptor must not be null.";
		}

		if (!VideoStreamLimits.IsValidStreamId(stream.Id))
		{
			return $"A stream id must be 1 to {VideoStreamLimits.MaxStreamIdLength} characters without control characters.";
		}

		if (stream.Name.IsEmpty)
		{
			return $"The name of stream '{stream.Id}' must not be empty.";
		}

		if (stream.Width < 0 || stream.Height < 0)
		{
			return $"The size of stream '{stream.Id}' must not be negative.";
		}

		return ValidateMap(stream.Metadata, "Metadata");
	}

	public static string? ValidateDescription(VideoStreamSessionDescription? description)
	{
		if (description is null)
		{
			return "A session description must not be null.";
		}

		if (!VideoStreamLimits.IsValidTransport(description.Transport))
		{
			return $"The transport must be 1 to {VideoStreamLimits.MaxTransportLength} characters of a-z, 0-9, '.', '+' and '-'.";
		}

		if (description.Url is { Length: > VideoStreamLimits.MaxUrlLength })
		{
			return $"The url must be at most {VideoStreamLimits.MaxUrlLength} characters.";
		}

		if (description.Payload is { Length: > VideoStreamLimits.MaxDescriptionPayloadLength })
		{
			return $"The payload must be at most {VideoStreamLimits.MaxDescriptionPayloadLength} characters.";
		}

		return ValidateMap(description.Parameters, "Parameters");
	}

	public static string? ValidateSignal(VideoStreamSignal? signal)
	{
		if (signal is null)
		{
			return "A signal must not be null.";
		}

		if (string.IsNullOrEmpty(signal.Type) || signal.Type.Length > VideoStreamLimits.MaxSignalTypeLength)
		{
			return $"A signal type must be 1 to {VideoStreamLimits.MaxSignalTypeLength} characters.";
		}

		if (signal.Payload is null || signal.Payload.Length > VideoStreamLimits.MaxSignalPayloadLength)
		{
			return $"A signal payload must be present and at most {VideoStreamLimits.MaxSignalPayloadLength} characters.";
		}

		return null;
	}

	public static ProtocolError ToError(VideoStreamErrorCode code, string message)
	{
		if (code == VideoStreamErrorCode.Unsupported)
		{
			return new ProtocolError
			{
				Code = ProtocolErrorCodes.CapabilityUnsupported, Message = message, Retryable = false
			};
		}

		var reason = ReasonFor(code);
		return new ProtocolError
		{
			Code = ProtocolErrorCodes.CapabilityUnavailable,
			Message = message,
			Retryable = code is VideoStreamErrorCode.Busy or VideoStreamErrorCode.CapacityReached or
				VideoStreamErrorCode.StreamUnavailable,
			Details = reason is null
				? null
				: new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = reason }
		};
	}

	public static VideoStreamErrorCode FromError(string code, string? reason)
		=> reason switch
		{
			ProtocolErrorReasons.VideoStreamUnknownProvider => VideoStreamErrorCode.UnknownProvider,
			ProtocolErrorReasons.VideoStreamUnknownStream => VideoStreamErrorCode.UnknownStream,
			ProtocolErrorReasons.VideoStreamUnknownSession => VideoStreamErrorCode.UnknownSession,
			ProtocolErrorReasons.VideoStreamStreamUnavailable => VideoStreamErrorCode.StreamUnavailable,
			ProtocolErrorReasons.VideoStreamTransportNotAccepted => VideoStreamErrorCode.TransportNotAccepted,
			ProtocolErrorReasons.VideoStreamCapacityReached => VideoStreamErrorCode.CapacityReached,
			ProtocolErrorReasons.VideoStreamSignalingUnsupported => VideoStreamErrorCode.SignalingUnsupported,
			ProtocolErrorReasons.VideoStreamBusy => VideoStreamErrorCode.Busy,
			_ when string.Equals(code, ProtocolErrorCodes.CapabilityUnsupported, StringComparison.Ordinal) =>
				VideoStreamErrorCode.Unsupported,
			_ => VideoStreamErrorCode.Failed
		};

	public static bool IsVideoStreamReason(string? reason)
		=> reason is not null && reason.StartsWith("video_stream_", StringComparison.Ordinal);

	private static string? ReasonFor(VideoStreamErrorCode code)
		=> code switch
		{
			VideoStreamErrorCode.UnknownProvider => ProtocolErrorReasons.VideoStreamUnknownProvider,
			VideoStreamErrorCode.UnknownStream => ProtocolErrorReasons.VideoStreamUnknownStream,
			VideoStreamErrorCode.UnknownSession => ProtocolErrorReasons.VideoStreamUnknownSession,
			VideoStreamErrorCode.StreamUnavailable => ProtocolErrorReasons.VideoStreamStreamUnavailable,
			VideoStreamErrorCode.TransportNotAccepted => ProtocolErrorReasons.VideoStreamTransportNotAccepted,
			VideoStreamErrorCode.CapacityReached => ProtocolErrorReasons.VideoStreamCapacityReached,
			VideoStreamErrorCode.SignalingUnsupported => ProtocolErrorReasons.VideoStreamSignalingUnsupported,
			VideoStreamErrorCode.Busy => ProtocolErrorReasons.VideoStreamBusy,
			_ => null
		};

	private static string? ValidateMap(IReadOnlyDictionary<string, string>? map, string name)
	{
		if (map is null)
		{
			return null;
		}

		if (map.Count > VideoStreamLimits.MaxMapEntries)
		{
			return $"{name} must have at most {VideoStreamLimits.MaxMapEntries} entries.";
		}

		foreach (var (key, value) in map)
		{
			if (string.IsNullOrEmpty(key) || key.Length > VideoStreamLimits.MaxMapKeyLength)
			{
				return $"A {name} key must be 1 to {VideoStreamLimits.MaxMapKeyLength} characters.";
			}

			if (value is null || value.Length > VideoStreamLimits.MaxMapValueLength)
			{
				return $"A {name} value must be present and at most {VideoStreamLimits.MaxMapValueLength} characters.";
			}
		}

		return null;
	}

	private static T Parse<T>(string? value, T fallback)
		where T : struct, Enum
		=> value is not null && Enum.GetNames<T>().Contains(value, StringComparer.Ordinal)
			? Enum.Parse<T>(value)
			: fallback;
}
