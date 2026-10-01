namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// Where Macro Deck fetches a session's media. Macro Deck's clients play <c>hls</c> and <c>mjpeg</c>, and
/// the host relays the media to them, so the source can bind to loopback and needs no credentials for the
/// clients. The provider's URL never reaches a client.
/// </summary>
/// <remarks>
/// Create one with <see cref="Hls" />, <see cref="Mjpeg" /> or <see cref="FromUrl" />. The description can
/// gain members and factories later without breaking a plugin built against this version.
/// </remarks>
public sealed class VideoStreamSessionDescription
{
	private const int MaxTransportLength = 32;
	private const int MaxUrlLength = 2 * 1024;

	private VideoStreamSessionDescription(string transport, string? url)
	{
		Transport = transport;
		Url = url;
	}

	/// <summary>
	/// A lowercase token naming how the media is delivered: 1 to 32 characters of <c>a-z</c>, <c>0-9</c>,
	/// <c>.</c>, <c>+</c> and <c>-</c>. Macro Deck plays <c>hls</c> and <c>mjpeg</c> and refuses any other
	/// transport with <see cref="VideoStreamErrorCode.TransportNotAccepted" />.
	/// </summary>
	public string Transport { get; }

	/// <summary>
	/// Where Macro Deck fetches the media: an absolute <c>http</c> or <c>https</c> URL of at most 2048
	/// characters, without user info and with no encoded slash (<c>%2F</c>) in its path. Required for
	/// <c>hls</c> and <c>mjpeg</c>. Null is reserved for source kinds added later, which carry no URL.
	/// </summary>
	public string? Url { get; }

	/// <summary>
	/// A description for any transport that is delivered from a URL. Macro Deck only accepts the transports
	/// it plays, so prefer <see cref="Hls" /> and <see cref="Mjpeg" />.
	/// </summary>
	/// <exception cref="ArgumentException">The transport or the URL breaks a bound documented on
	/// <see cref="Transport" /> and <see cref="Url" />.</exception>
	public static VideoStreamSessionDescription FromUrl(string transport, string url)
	{
		if (!IsValidTransport(transport))
		{
			throw new ArgumentException(
				$"The transport must be 1 to {MaxTransportLength} characters of a-z, 0-9, '.', '+' and '-'.",
				nameof(transport));
		}

		if (UrlProblem(url) is { } problem)
		{
			throw new ArgumentException(problem, nameof(url));
		}

		return new VideoStreamSessionDescription(transport, url);
	}

	/// <summary>An HLS stream: the URL of its playlist. Segments must stay on the playlist's origin.</summary>
	/// <exception cref="ArgumentException">The URL breaks a bound documented on <see cref="Url" />.</exception>
	public static VideoStreamSessionDescription Hls(string url) => FromUrl("hls", url);

	/// <summary>A Motion JPEG stream: the URL of a <c>multipart/x-mixed-replace</c> response.</summary>
	/// <exception cref="ArgumentException">The URL breaks a bound documented on <see cref="Url" />.</exception>
	public static VideoStreamSessionDescription Mjpeg(string url) => FromUrl("mjpeg", url);

	// Mirrors VideoStreamLimits and the structural check in MacroDeck.Plugin.Hosting; this project
	// cannot reference either, and a test keeps them in step.
	private static bool IsValidTransport(string? transport)
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

	private static string? UrlProblem(string? url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return "The url must not be empty.";
		}

		if (url.Length > MaxUrlLength)
		{
			return $"The url must be at most {MaxUrlLength} characters.";
		}

		var schemeLength = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8
			: url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7
			: 0;
		if (schemeLength == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
		{
			return "The url must be an absolute http or https URL.";
		}

		var authorityStart = schemeLength;
		var authorityEnd = url.AsSpan(authorityStart).IndexOfAny('/', '?', '#');
		var authority = authorityEnd < 0 ? url.AsSpan(authorityStart) : url.AsSpan(authorityStart, authorityEnd);
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
}
