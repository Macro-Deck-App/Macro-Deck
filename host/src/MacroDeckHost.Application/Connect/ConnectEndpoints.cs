namespace MacroDeckHost.Application.Connect;

/// <summary>Verified constants for the Macro Deck Connect identity provider. Do not change these
/// without re-verifying against the issuer - they are a compatibility commitment.</summary>
public static class ConnectEndpoints
{
	public const string Issuer = "https://auth.macro-deck.app";
	public const string TokenEndpoint = Issuer + "/oauth/v2/token";
	public const string DeviceAuthorizationEndpoint = Issuer + "/oauth/v2/device_authorization";
	public const string RevokeEndpoint = Issuer + "/oauth/v2/revoke";

	public const string ClientId = "390578325090796895";
	public const string Scope = "openid profile offline_access";

	public const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

	public const string AccountManagementUrl = Issuer + "/ui/console/users/me";
}
