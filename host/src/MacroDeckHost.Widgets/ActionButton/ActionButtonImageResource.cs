using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>
/// Decodes the legacy <c>imageUrl</c> field - a URL or a data URI - into raw bytes a <c>ui.button</c>
/// tree can draw as its backdrop, without ever making the host fetch a stored URL. Issue #748's resolved
/// decision: a data URI is decoded here; an <c>http(s)</c> URL is ignored outright, because a widget tree
/// must not cause an arbitrary stored URL to be fetched on every session open.
/// </summary>
internal static class ActionButtonImageResource
{
	public static bool TryParse(string? imageUrl, out ReadOnlyMemory<byte> content, out string mediaType)
	{
		content = default;
		mediaType = string.Empty;

		if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var commaIndex = imageUrl.IndexOf(',');

		if (commaIndex < 0)
		{
			return false;
		}

		var header = imageUrl[5..commaIndex];
		var payload = imageUrl[(commaIndex + 1)..];

		var headerParts = header.Split(';');
		var declaredMediaType = headerParts.Length > 0 && headerParts[0].Length > 0
			? headerParts[0]
			: "application/octet-stream";
		var isBase64 = headerParts.Any(part => string.Equals(part, "base64", StringComparison.OrdinalIgnoreCase));

		if (!isBase64)
		{
			// Every image data URI this feature has ever written is base64 - a percent-encoded text
			// payload is not a shape any client has produced, and not worth the extra decode path.
			return false;
		}

		byte[] bytes;

		try
		{
			bytes = Convert.FromBase64String(payload);
		}
		catch (FormatException)
		{
			return false;
		}

		if (bytes.Length == 0 || bytes.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			return false;
		}

		content = bytes;
		mediaType = declaredMediaType;

		return true;
	}
}
