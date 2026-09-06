using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal static partial class YtmDesktopUrls
{
	private static readonly string[] _youtubeHosts =
	[
		"music.youtube.com", "www.youtube.com", "youtube.com", "m.youtube.com", "youtu.be"
	];

	public static bool TryParse(string? value, out string? videoId, out string? playlistId)
	{
		videoId = null;
		playlistId = null;

		var trimmed = value?.Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			return false;
		}

		if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
			(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
			_youtubeHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
		{
			ParseYoutubeUrl(uri, out videoId, out playlistId);
			return videoId is not null || playlistId is not null;
		}

		if (VideoIdPattern().IsMatch(trimmed))
		{
			videoId = trimmed;
		}
		else
		{
			playlistId = trimmed;
		}

		return true;
	}

	private static void ParseYoutubeUrl(Uri uri, out string? videoId, out string? playlistId)
	{
		videoId = null;
		playlistId = null;

		foreach (var (key, value) in EnumerateQuery(uri.Query))
		{
			if (string.Equals(key, "v", StringComparison.Ordinal))
			{
				videoId = value;
			}
			else if (string.Equals(key, "list", StringComparison.Ordinal))
			{
				playlistId = value;
			}
		}

		var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase))
		{
			videoId ??= segments.Length > 0 ? segments[0] : null;
			return;
		}

		for (var i = 0; i < segments.Length - 1; i++)
		{
			if (!string.Equals(segments[i], "browse", StringComparison.Ordinal))
			{
				continue;
			}

			var id = segments[i + 1];
			playlistId ??= id.StartsWith("VL", StringComparison.Ordinal) ? id[2..] : id;
			break;
		}
	}

	private static IEnumerable<(string Key, string Value)> EnumerateQuery(string query)
	{
		if (query.Length == 0)
		{
			yield break;
		}

		var body = query[0] == '?' ? query[1..] : query;
		foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = pair.IndexOf('=');
			var key = separator < 0 ? pair : pair[..separator];
			var value = separator < 0 ? string.Empty : pair[(separator + 1)..];

			yield return (Uri.UnescapeDataString(key), Uri.UnescapeDataString(value));
		}
	}

	[GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
	private static partial Regex VideoIdPattern();
}
