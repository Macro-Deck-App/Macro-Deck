namespace MacroDeckHost.Integrations.Jellyfin.Configuration;

internal static class JellyfinKeys
{
	public static string Stem(string text, string fallback, int maxLength)
	{
		var stem = ConfigurationIdentityAllocator.Sanitize(text);
		if (stem.Length > maxLength)
		{
			stem = stem[..maxLength].TrimEnd('_');
		}

		return stem.Length == 0 ? fallback : stem;
	}
}
