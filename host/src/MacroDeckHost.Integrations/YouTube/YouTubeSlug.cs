using System.Text;

namespace MacroDeckHost.Integrations.YouTube;

internal static class YouTubeSlug
{
	public static string ForChannel(string? handle, string? title, string channelId)
		=> Slug(handle?.TrimStart('@')) ?? Slug(title) ?? ForChannelId(channelId);

	public static string ForChannelId(string channelId)
	{
		var builder = new StringBuilder("ch", channelId.Length + 2);

		foreach (var character in channelId.ToLowerInvariant())
		{
			builder.Append(character is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? character : '_');
		}

		return builder.ToString();
	}

	private static string? Slug(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		var builder = new StringBuilder(text.Length);
		var lastWasSeparator = false;

		foreach (var character in text.ToLowerInvariant())
		{
			if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
			{
				builder.Append(character);
				lastWasSeparator = false;
				continue;
			}

			if (!lastWasSeparator && builder.Length > 0)
			{
				builder.Append('_');
				lastWasSeparator = true;
			}
		}

		var slug = builder.ToString().Trim('_');

		return slug.Length > 0 ? slug : null;
	}
}
