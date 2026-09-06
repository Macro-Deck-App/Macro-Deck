using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Auth;

/// <summary>
/// Reads the auth cookies for the listener a request arrived on. Cookies are scoped to a host name
/// and ignore the port, so two Macro Deck hosts on one machine - an installed one beside a
/// development build - share a cookie jar. Under one shared name the last writer wins, and the other
/// installation's <c>&lt;img&gt;</c> requests, which have no way to carry a header, then present a
/// token signed with a key that host never issued.
/// </summary>
public static class AuthCookies
{
	/// <summary>
	/// The port the client addressed, which is what the browser scopes the cookie by together with the
	/// host name. Falls back to the socket's own port when there is no Host header to read.
	/// </summary>
	public static int ListenerPort(HttpContext context)
		=> context.Request.Host.Port ?? context.Connection.LocalPort;

	public static string? ReadAccessToken(HttpContext context)
		=> Read(context, AuthDefaults.AccessCookieFor(ListenerPort(context)), AuthDefaults.AccessCookie);

	public static string? ReadRefreshToken(HttpContext context)
		=> Read(context, AuthDefaults.RefreshCookieFor(ListenerPort(context)), AuthDefaults.RefreshCookie);

	// The unsuffixed name is what installations wrote before the cookies were named per listener. Read
	// as a fallback so an upgrade does not end every session that is currently signed in; the next sign-in
	// writes the suffixed one and the collision is gone from then on.
	private static string? Read(HttpContext context, string preferred, string legacy)
	{
		var value = context.Request.Cookies[preferred];

		return string.IsNullOrEmpty(value) ? context.Request.Cookies[legacy] : value;
	}
}
