using MacroDeckHost.Integrations.Obs;

namespace MacroDeckHost.Integrations.Jellyfin;

internal static class JellyfinKeys
{
	public static string Stem(string text, string fallback, int maxLength)
	{
		var stem = ObsConfigurationIdentityAllocator.Sanitize(text);
		if (stem.Length > maxLength)
		{
			stem = stem[..maxLength].TrimEnd('_');
		}

		return stem.Length == 0 ? fallback : stem;
	}
}
