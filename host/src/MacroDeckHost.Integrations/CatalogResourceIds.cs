using System.Globalization;
using System.Text;

namespace MacroDeckHost.Integrations;

// Percent-encoding for the runtime names a catalog resource id is built out of. MacroDeckId.Resource
// bans whitespace, control characters and "::", and the path-shaped id formats built on top of it also
// use '/' as their own separator. Encoding those four kinds of character - and only those - over the
// name's UTF-8 bytes is what lets "Mic Aux", "Mic_Aux" and "Mic  Aux" round-trip to three distinct ids
// instead of collapsing onto one: a plain "replace whitespace with _" scheme could not tell them apart
// again.
internal static class CatalogResourceIds
{
	public static string Encode(string value)
	{
		StringBuilder? builder = null;
		for (var i = 0; i < value.Length; i++)
		{
			var character = value[i];
			if (character is '%' or ':' or '/' || char.IsWhiteSpace(character) || char.IsControl(character))
			{
				builder ??= new StringBuilder(value, 0, i, value.Length + 8);
				foreach (var b in Encoding.UTF8.GetBytes(character.ToString()))
				{
					builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
				}
			}
			else
			{
				builder?.Append(character);
			}
		}

		return builder?.ToString() ?? value;
	}

	public static bool TryDecode(string encoded, out string value)
	{
		if (encoded.IndexOf('%') < 0)
		{
			value = encoded;
			return true;
		}

		var bytes = new List<byte>(encoded.Length);
		var i = 0;
		while (i < encoded.Length)
		{
			if (encoded[i] == '%')
			{
				if (i + 2 >= encoded.Length ||
					!TryHexDigit(encoded[i + 1], out var high) ||
					!TryHexDigit(encoded[i + 2], out var low))
				{
					value = string.Empty;
					return false;
				}

				bytes.Add((byte)((high << 4) | low));
				i += 3;
				continue;
			}

			var start = i;
			while (i < encoded.Length && encoded[i] != '%')
			{
				i++;
			}

			bytes.AddRange(Encoding.UTF8.GetBytes(encoded, start, i - start));
		}

		try
		{
			value = new UTF8Encoding(false, true).GetString(bytes.ToArray());
			return true;
		}
		catch (DecoderFallbackException)
		{
			value = string.Empty;
			return false;
		}
	}

	private static bool TryHexDigit(char character, out int value)
	{
		switch (character)
		{
			case >= '0' and <= '9':
				value = character - '0';
				return true;
			case >= 'A' and <= 'F':
				value = character - 'A' + 10;
				return true;
			case >= 'a' and <= 'f':
				value = character - 'a' + 10;
				return true;
			default:
				value = 0;
				return false;
		}
	}
}
