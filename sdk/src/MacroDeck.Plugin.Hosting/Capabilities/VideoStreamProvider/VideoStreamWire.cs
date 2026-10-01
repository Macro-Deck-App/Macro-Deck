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
		=> new() { Transport = description.Transport, Url = description.Url };

	public static VideoStreamSessionDescription ToDescription(VideoStreamSessionDescriptionDto dto)
		=> VideoStreamSessionDescription.FromUrl(dto.Transport,
			dto.Url ?? throw new ArgumentException("The description has no url.", nameof(dto)));

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

	// Structural only: whether a transport is one the host plays is the host's call, so a new transport
	// needs no SDK release. Hls and mjpeg are named only because they cannot work without a Url.
	public static string? ValidateDescription(VideoStreamSessionDescriptionDto? description)
	{
		if (description is null)
		{
			return "A session description must not be null.";
		}

		if (!VideoStreamLimits.IsValidTransport(description.Transport))
		{
			return $"The transport must be 1 to {VideoStreamLimits.MaxTransportLength} characters of a-z, 0-9, '.', '+' and '-'.";
		}

		if (description.Url is null)
		{
			return description.Transport is "hls" or "mjpeg"
				? $"The {description.Transport} transport requires a url."
				: null;
		}

		return ValidateUrl(description.Url);
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
			VideoStreamErrorCode.Busy => ProtocolErrorReasons.VideoStreamBusy,
			_ => null
		};

	private static string? ValidateUrl(string url)
	{
		if (url.Length > VideoStreamLimits.MaxUrlLength)
		{
			return $"The url must be at most {VideoStreamLimits.MaxUrlLength} characters.";
		}

		var schemeLength = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8
			: url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7
			: 0;
		if (schemeLength == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
		{
			return "The url must be an absolute http or https URL.";
		}

		var authorityEnd = url.AsSpan(schemeLength).IndexOfAny('/', '?', '#');
		var authority = authorityEnd < 0 ? url.AsSpan(schemeLength) : url.AsSpan(schemeLength, authorityEnd);
		if (authority.Contains('@') || uri.UserInfo.Length > 0)
		{
			return "The url must not contain user info.";
		}

		var pathEnd = url.AsSpan().IndexOfAny('?', '#');
		var beforeQuery = pathEnd < 0 ? url.AsSpan() : url.AsSpan(0, pathEnd);
		if (beforeQuery.Contains('\\') || beforeQuery.Contains("%2f", StringComparison.OrdinalIgnoreCase))
		{
			return "The url path must not contain a backslash or an encoded slash (%2F).";
		}

		return null;
	}

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
