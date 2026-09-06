namespace MacroDeck.Sdk.Actions;

/// <summary>
/// Failure reasons an integration reports through <see cref="ActionResult.Failed"/>. Shared so the
/// same condition carries the same code across integrations and a client can react to it.
/// </summary>
public static class ActionErrorCodes
{
	/// <summary>The integration has no usable configuration - no account, no instance, nothing set up.</summary>
	public const string NotConfigured = "NOT_CONFIGURED";

	/// <summary>Configured, but the provider is not reachable right now.</summary>
	public const string NotConnected = "NOT_CONNECTED";

	/// <summary>The provider refused for lack of a scope, grant or OS permission.</summary>
	public const string PermissionDenied = "PERMISSION_DENIED";

	/// <summary>The provider errored - an unexpected response, a broken call.</summary>
	public const string ProviderError = "PROVIDER_ERROR";

	/// <summary>The provider understood the request and declined it.</summary>
	public const string ProviderRejected = "PROVIDER_REJECTED";

	/// <summary>A parameter is missing, malformed or unusable.</summary>
	public const string InvalidParameter = "INVALID_PARAMETER";

	/// <summary>What the action targets does not exist.</summary>
	public const string NotFound = "NOT_FOUND";

	public const string Timeout = "TIMEOUT";

	/// <summary>The operation is not available here - wrong platform, unsupported provider version.</summary>
	public const string Unavailable = "UNAVAILABLE";
}
