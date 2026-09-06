using System.Text;

namespace MacroDeckHost.Integrations.Twitch;

internal static class TwitchSlug
{
	public static string ForLogin(string login, string userId)
	{
		var builder = new StringBuilder(login.Length);
		var lastWasSeparator = false;

		foreach (var character in login.ToLowerInvariant())
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

		return slug.Length > 0 ? slug : $"id{userId}";
	}
}
