using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.Steam.Configuration;

internal readonly record struct SteamIdentity(string? SteamId, string? VanityName)
{
	private static readonly Regex _steamId64 = new(@"^7656119\d{10}$", RegexOptions.CultureInvariant);

	private static readonly Regex _vanity = new(@"^[A-Za-z0-9_\-]{2,32}$", RegexOptions.CultureInvariant);

	public bool IsValid => SteamId is not null || VanityName is not null;

	public static SteamIdentity Parse(string? input)
	{
		var text = input?.Trim() ?? string.Empty;
		if (text.Length == 0)
		{
			return default;
		}

		if (_steamId64.IsMatch(text))
		{
			return new SteamIdentity(text, null);
		}

		if (_vanity.IsMatch(text) && !text.All(char.IsAsciiDigit))
		{
			return new SteamIdentity(null, text);
		}

		if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
			&& string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
			&& uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is [var kind, var value, ..])
		{
			if (string.Equals(kind, "profiles", StringComparison.OrdinalIgnoreCase) && _steamId64.IsMatch(value))
			{
				return new SteamIdentity(value, null);
			}

			if (string.Equals(kind, "id", StringComparison.OrdinalIgnoreCase) && _vanity.IsMatch(value))
			{
				return new SteamIdentity(null, value);
			}
		}

		return default;
	}
}
