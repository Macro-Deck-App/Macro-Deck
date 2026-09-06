namespace MacroDeck.Plugin.Protocol.Auth;

/// <summary>
/// Header names, scope value and lifetimes for plugin authentication. <see cref="SessionTokenLifetime" />
/// mirrors the host's <c>AuthDefaults.AccessTokenLifetime</c>, duplicated rather than referenced -
/// this project never references the host.
/// </summary>
public static class PluginAuthDefaults
{
	public const string PluginIdHeaderName = "X-MacroDeck-Plugin-Id";

	/// <summary>Session exchange only - never sent on the WebSocket upgrade.</summary>
	public const string PluginSecretHeaderName = "X-MacroDeck-Plugin-Secret";

	public const string EnrollmentTokenHeaderName = "X-MacroDeck-Enrollment-Token";

	public const string SessionIdHeaderName = "X-MacroDeck-Session-Id";

	public const string AuthorizationHeaderName = "Authorization";

	public const string BearerScheme = "Bearer";

	public const string PluginScope = "plugin";

	public static readonly TimeSpan SessionTokenLifetime = TimeSpan.FromMinutes(15);

	/// <summary>32 random bytes, base64url-encoded.</summary>
	public const int MinPluginSecretLength = 43;

	public static readonly IReadOnlyList<string> All =
	[
		PluginIdHeaderName, PluginSecretHeaderName, EnrollmentTokenHeaderName, SessionIdHeaderName,
		AuthorizationHeaderName,
	];
}
