using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal static class StreamlabsTokenReader
{
	internal readonly record struct Connection(string Token, int? Port);

	public static Connection? Parse(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return null;
		}

		var trimmed = input.Trim();

		if (trimmed[0] == '{')
		{
			return TryReadJson(trimmed, out var fromJson) ? fromJson : null;
		}

		if (TryDecodeBase64(trimmed, out var decoded) && TryReadJson(decoded, out var fromBlob))
		{
			return fromBlob;
		}

		return new Connection(trimmed, null);
	}

	private static bool TryReadJson(string text, out Connection connection)
	{
		connection = default;
		if (text.Length == 0 || text[0] != '{')
		{
			return false;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(text);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			var root = document.RootElement;
			if (StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(root, "token")) is not { } token ||
				string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			var port = StreamlabsModelReader.ReadNumber(StreamlabsModelReader.Property(root, "port"));
			connection = new Connection(token.Trim(),
				port is > 0 and <= 65535 ? (int)port.Value : null);
			return true;
		}
	}

	private static bool TryDecodeBase64(string text, out string decoded)
	{
		decoded = string.Empty;

		var buffer = new byte[((text.Length * 3) / 4) + 3];
		if (!Convert.TryFromBase64String(text, buffer, out var written) || written == 0)
		{
			return false;
		}

		try
		{
			decoded = new UTF8Encoding(false, true).GetString(buffer, 0, written).Trim();
			return true;
		}
		catch (DecoderFallbackException)
		{
			return false;
		}
	}
}
