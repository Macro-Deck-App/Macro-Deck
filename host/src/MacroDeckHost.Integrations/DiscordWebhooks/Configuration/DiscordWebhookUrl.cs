using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Configuration;

internal static partial class DiscordWebhookUrl
{
	private static readonly string[] _hosts =
		["discord.com", "discordapp.com", "ptb.discord.com", "canary.discord.com"];

	public static bool TryParse(string? value, out Uri uri)
	{
		uri = null!;
		if (string.IsNullOrWhiteSpace(value)
			|| !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
			|| parsed.Scheme != Uri.UriSchemeHttps
			|| !parsed.IsDefaultPort
			|| parsed.UserInfo.Length > 0
			|| !_hosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase)
			|| !PathRegex().IsMatch(parsed.AbsolutePath))
		{
			return false;
		}

		uri = parsed;
		return true;
	}

	[GeneratedRegex("""\A/api/(?:v\d+/)?webhooks/\d{1,20}/[A-Za-z0-9_\-]+/?\z""", RegexOptions.CultureInvariant)]
	private static partial Regex PathRegex();
}
