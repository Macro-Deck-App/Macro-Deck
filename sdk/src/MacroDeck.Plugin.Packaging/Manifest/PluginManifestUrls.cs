namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// The single definition of "an absolute http or https URL" every manifest URL field is checked against.
/// Extracted from <see cref="PluginManifestReader" /> so <c>MacroDeck.Plugin.Cli</c>'s scaffolding
/// validation (<c>macrodeck-plugin new</c>) can reuse the exact same rule instead of restating it - a
/// second copy is how the reader and the CLI's own pre-flight checks would drift apart on what a legal
/// URL is.
/// </summary>
internal static class PluginManifestUrls
{
	public static bool IsAbsoluteHttpUrl(string value)
		=> Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
			(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
				string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
