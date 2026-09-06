namespace MacroDeck.Localization.Compiler;

/// <summary>Validates the culture suffix of a resource file name.</summary>
/// <remarks>
/// A deliberate shape check rather than a <c>CultureInfo</c> lookup: under ICU, .NET happily manufactures
/// a culture for any well-formed-looking name, so <c>CultureInfo.GetCultureInfo("zz-QQ")</c> succeeds and
/// would make MDLOC005 unable to fire at all.
/// </remarks>
internal static class LocalizationCultureName
{
	/// <summary>Whether <paramref name="culture" /> is a well-formed BCP-47 name of the shapes resources
	/// use: <c>de</c>, <c>de-DE</c>, <c>zh-Hans</c> or <c>zh-Hans-CN</c>.</summary>
	public static bool IsValid(string? culture)
	{
		if (string.IsNullOrEmpty(culture))
		{
			return false;
		}

		var parts = culture!.Split('-');
		if (parts.Length < 1 || parts.Length > 3)
		{
			return false;
		}

		if (!IsAlpha(parts[0]) || parts[0].Length < 2 || parts[0].Length > 3)
		{
			return false;
		}

		if (parts.Length == 1)
		{
			return true;
		}

		var next = 1;
		if (parts[next].Length == 4 && IsAlpha(parts[next]))
		{
			next++;
		}

		if (next == parts.Length)
		{
			return true;
		}

		if (next != parts.Length - 1)
		{
			return false;
		}

		var region = parts[next];
		return (region.Length == 2 && IsAlpha(region)) || (region.Length == 3 && IsDigits(region));
	}

	/// <summary>The neutral culture of <paramref name="culture" />, or <c>null</c> when it is already
	/// neutral - <c>de-DE</c> yields <c>de</c>, <c>de</c> yields <c>null</c>.</summary>
	public static string? NeutralOf(string? culture)
	{
		if (string.IsNullOrEmpty(culture))
		{
			return null;
		}

		var separator = culture!.IndexOf('-');
		return separator > 0 ? culture.Substring(0, separator) : null;
	}

	private static bool IsAlpha(string value)
	{
		if (value.Length == 0)
		{
			return false;
		}

		foreach (var character in value)
		{
			if (!char.IsLetter(character))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsDigits(string value)
	{
		foreach (var character in value)
		{
			if (!char.IsDigit(character))
			{
				return false;
			}
		}

		return true;
	}
}
