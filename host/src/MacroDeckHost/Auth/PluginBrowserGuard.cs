namespace MacroDeckHost.Auth;

public static class PluginBrowserGuard
{
	private const string SecFetchSiteHeader = "Sec-Fetch-Site";
	private const string OriginHeader = "Origin";

	public static bool IsBrowserRequest(HttpContext context)
	{
		var request = context.Request;

		if (!string.IsNullOrEmpty(request.Headers[OriginHeader]))
		{
			return true;
		}

		var secFetchSite = request.Headers[SecFetchSiteHeader].ToString();
		return string.Equals(secFetchSite, "cross-site", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(secFetchSite, "same-site", StringComparison.OrdinalIgnoreCase);
	}
}
