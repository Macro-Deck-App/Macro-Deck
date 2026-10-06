using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Integrations.GoogleCalendar;

internal static class GoogleOAuth
{
	public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
	public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
	public const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
	public const string CalendarScope = "https://www.googleapis.com/auth/calendar.readonly";
	public const string RequestedScopes = CalendarScope + " openid email";
	public const string ConsoleUrl = "https://console.cloud.google.com/apis/credentials";
	public const string CalendarApiUrl = "https://console.cloud.google.com/apis/library/calendar-json.googleapis.com";

	public static string CreateCodeVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

	public static string CodeChallengeFor(string verifier)
		=> Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

	public static string AuthorizeUrl(string clientId, string redirectUri, string state, string codeVerifier)
	{
		var query = new (string Name, string Value)[]
		{
			("response_type", "code"),
			("client_id", clientId),
			("redirect_uri", redirectUri),
			("scope", RequestedScopes),
			("access_type", "offline"),
			("prompt", "consent"),
			("state", state),
			("code_challenge", CodeChallengeFor(codeVerifier)),
			("code_challenge_method", "S256")
		};

		return AuthorizeEndpoint + "?" +
			string.Join('&', query.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
	}

	public static bool GrantsCalendarAccess(string? scope)
		=> scope is not null &&
			scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(CalendarScope, StringComparer.Ordinal);
}
