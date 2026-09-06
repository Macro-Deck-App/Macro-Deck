namespace MacroDeckHost.Application.Connect;

/// <summary>Verified constants for the Macro Deck Connect identity provider. Do not change these
/// without re-verifying against the issuer - they are a compatibility commitment.</summary>
public static class ConnectEndpoints
{
	public const string Issuer = "https://accounts.macro-deck.app/";
	public const string TokenEndpoint = "https://accounts.macro-deck.app/connect/token";
	public const string DeviceAuthorizationEndpoint = "https://accounts.macro-deck.app/connect/device";
	public const string RevokeEndpoint = "https://accounts.macro-deck.app/connect/revoke";

	public const string ClientId = "macrodeck-app";
	public const string Scope = "openid profile offline_access";

	public const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

	public const string AccountManagementUrl = "https://accounts.macro-deck.app/";

	public const string AvatarBaseUrl = "https://accounts.macro-deck.app/avatars/";

	/// <summary>
	/// The issuer's opaque avatar id, which changes whenever the user replaces their picture. Clients
	/// receive this rather than the upstream URL, so a new picture busts their cache of the host's own
	/// fixed avatar route.
	/// </summary>
	public static string? AvatarVersionOf(string? pictureUrl)
	{
		if (pictureUrl is null)
		{
			return null;
		}

		var fileName = pictureUrl.Split('/')[^1];
		var id = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
			? fileName[..^4]
			: fileName;

		return id.Length == 0 ? null : id;
	}
}
