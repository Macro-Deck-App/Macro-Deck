using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

internal static partial class MicrosoftOAuth
{
	public const string AuthorityBase = "https://login.microsoftonline.com/";
	public const string DefaultTenant = "common";

	// Macro Deck's own multi-tenant public client: an app id is not a secret, and no client secret exists.
	public const string DefaultClientId = "9c118388-24e0-4212-9eae-ecc52da6b9b1";
	public const string CalendarScope = "https://graph.microsoft.com/Calendars.Read";
	public const string RequestedScopes = "openid profile email offline_access " + CalendarScope;
	public static string EffectiveClientId(string? clientId)
		=> string.IsNullOrWhiteSpace(clientId) ? DefaultClientId : clientId.Trim();

	public static string CreateCodeVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

	public static string CodeChallengeFor(string verifier)
		=> Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

	public static string NormalizeTenant(string? tenant)
		=> string.IsNullOrWhiteSpace(tenant) ? DefaultTenant : tenant.Trim().ToLowerInvariant();

	public static bool IsValidTenant(string tenant)
		=> tenant is "common" or "organizations" or "consumers" ||
			Guid.TryParse(tenant, out _) ||
			DomainName().IsMatch(tenant);

	public static string TokenEndpoint(string tenant)
		=> $"{AuthorityBase}{Uri.EscapeDataString(NormalizeTenant(tenant))}/oauth2/v2.0/token";

	public static string AuthorizeUrl(
		string clientId,
		string tenant,
		string redirectUri,
		string state,
		string codeVerifier)
	{
		var query = new (string Name, string Value)[]
		{
			("client_id", clientId),
			("response_type", "code"),
			("redirect_uri", redirectUri),
			("response_mode", "query"),
			("scope", RequestedScopes),
			("prompt", "select_account"),
			("state", state),
			("code_challenge", CodeChallengeFor(codeVerifier)),
			("code_challenge_method", "S256")
		};

		return $"{AuthorityBase}{Uri.EscapeDataString(NormalizeTenant(tenant))}/oauth2/v2.0/authorize?" +
			string.Join('&', query.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
	}

	// Microsoft answers the granted scope with or without the Graph resource prefix.
	public static bool GrantsCalendarAccess(string? scope)
		=> scope is not null &&
			scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Any(granted => string.Equals(granted, CalendarScope, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(granted, "Calendars.Read", StringComparison.OrdinalIgnoreCase));

	[GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
	private static partial Regex DomainName();
}
