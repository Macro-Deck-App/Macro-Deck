namespace MacroDeckHost.Integrations.AdGuardHome;

internal static class AdGuardHomeEndpoint
{
	public const string ExampleUrl = "http://192.168.1.2:3000";

	private const string ControlSegment = "/control";

	public static Uri? TryBuild(string? baseUrl)
	{
		var value = (baseUrl ?? string.Empty).Trim();

		var cut = value.IndexOfAny(['?', '#']);
		if (cut >= 0)
		{
			value = value[..cut].Trim();
		}

		if (value.Length == 0)
		{
			return null;
		}

		if (!value.Contains("://", StringComparison.Ordinal))
		{
			value = "http://" + value;
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
			string.IsNullOrEmpty(parsed.Host) ||
			parsed.Scheme is not ("http" or "https"))
		{
			return null;
		}

		var path = parsed.AbsolutePath.TrimEnd('/');
		if (path.EndsWith(ControlSegment, StringComparison.OrdinalIgnoreCase))
		{
			path = path[..^ControlSegment.Length];
		}

		return new UriBuilder(parsed.Scheme, parsed.Host, parsed.IsDefaultPort ? -1 : parsed.Port, path + ControlSegment + "/")
			.Uri;
	}
}
