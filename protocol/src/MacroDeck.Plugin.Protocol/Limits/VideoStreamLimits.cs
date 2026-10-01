namespace MacroDeck.Plugin.Protocol.Limits;

/// <summary>
/// Bounds of the <c>video-stream-provider</c> capability kind and the <c>video-streams</c> host api. The
/// SDK rejects a value past a bound with an <see cref="ArgumentException" /> before it is sent, and the host
/// answers <c>INVALID_PAYLOAD</c> for one that arrives anyway. Text lengths count UTF-16 code units, the
/// way <see cref="string.Length" /> does.
/// </summary>
public static class VideoStreamLimits
{
	public const int MaxProvidersPerPlugin = 16;

	/// <summary>A provider listing more streams is cut to the first ones by the host, with a warning.</summary>
	public const int MaxStreamsPerProvider = 256;

	public const int MaxStreamIdLength = 256;

	/// <summary>Per <c>Metadata</c> dictionary.</summary>
	public const int MaxMapEntries = 32;

	public const int MaxMapKeyLength = 64;

	public const int MaxMapValueLength = 2 * 1024;

	public const int MaxTransportLength = 32;

	public const int MaxUrlLength = 2 * 1024;

	/// <summary>A transport token: 1 to <see cref="MaxTransportLength" /> of <c>a-z</c>, <c>0-9</c>,
	/// <c>.</c>, <c>+</c> and <c>-</c>.</summary>
	public static bool IsValidTransport(string? transport)
	{
		if (string.IsNullOrEmpty(transport) || transport.Length > MaxTransportLength)
		{
			return false;
		}

		foreach (var c in transport)
		{
			if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '+' or '-'))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>A stream id: 1 to <see cref="MaxStreamIdLength" /> characters, none of them a control
	/// character. Spaces are allowed, since stream ids are often names from the source.</summary>
	public static bool IsValidStreamId(string? streamId)
		=> !string.IsNullOrEmpty(streamId) &&
			streamId.Length <= MaxStreamIdLength &&
			!streamId.Any(char.IsControl);
}
