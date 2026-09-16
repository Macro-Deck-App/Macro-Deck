namespace MacroDeckHost.Application.Connect;

public static class ConnectAssetUrls
{
	private static readonly Uri _issuer = new(ConnectEndpoints.Issuer);

	// The host fetches with its own network position, so only the issuer's public asset route is allowed.
	public static bool IsTrusted(string? url)
		=> Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
			uri.Scheme == Uri.UriSchemeHttps &&
			string.Equals(uri.Host, _issuer.Host, StringComparison.OrdinalIgnoreCase) &&
			uri.IsDefaultPort &&
			uri.UserInfo.Length == 0 &&
			uri.AbsolutePath.StartsWith("/assets/", StringComparison.Ordinal);
}
