using System.Globalization;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// Reads the two forms Json.NET produced for Macro Deck 2's <c>System.Drawing.Color</c> values: a known
/// colour's name ("Lime") and a bare "R, G, B" triple for everything else. Alpha never travelled, so a
/// parsed colour is always opaque.
/// </summary>
internal static class MacroDeck2Color
{
	public static string? ToHex(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var trimmed = value.Trim();

		if (trimmed.StartsWith('#'))
		{
			return trimmed.Length is 4 or 7 ? trimmed.ToLowerInvariant() : null;
		}

		if (trimmed.Contains(',', StringComparison.Ordinal))
		{
			return FromTriple(trimmed);
		}

		return FromName(trimmed);
	}

	private static string? FromTriple(string value)
	{
		var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		// Four components are "A, R, G, B"; alpha is dropped because Macro Deck 3 button colours are opaque.
		var components = parts.Length switch
		{
			3 => parts,
			4 => parts[1..],
			_ => null
		};

		if (components is null)
		{
			return null;
		}

		Span<int> channels = stackalloc int[3];
		for (var index = 0; index < 3; index++)
		{
			if (!int.TryParse(components[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel) ||
				channel is < 0 or > 255)
			{
				return null;
			}

			channels[index] = channel;
		}

		return $"#{channels[0]:x2}{channels[1]:x2}{channels[2]:x2}";
	}

	private static string? FromName(string name)
	{
		var known = System.Drawing.Color.FromName(name);
		return known.IsKnownColor ? $"#{known.R:x2}{known.G:x2}{known.B:x2}" : null;
	}
}
