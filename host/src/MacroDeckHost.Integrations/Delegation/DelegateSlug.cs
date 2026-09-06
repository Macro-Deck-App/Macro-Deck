using System.Text;

namespace MacroDeckHost.Integrations.Delegation;

internal static class DelegateSlug
{
	public static string For(string? machineName, string instanceId)
	{
		var builder = new StringBuilder();
		var lastWasSeparator = false;

		foreach (var character in (machineName ?? string.Empty).ToLowerInvariant())
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

		return slug.Length > 0 ? slug : $"id{instanceId}";
	}
}
