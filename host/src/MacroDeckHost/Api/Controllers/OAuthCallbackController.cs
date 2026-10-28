using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/integrations/oauth")]
public class OAuthCallbackController : ControllerBase
{
	private const string PageStyle
		= "body { font-family: system-ui, sans-serif; background: #121212; color: #fff; display: flex; "
		+ "align-items: center; justify-content: center; height: 100vh; margin: 0; } "
		+ ".card { text-align: center; padding: 2rem 2.5rem; } "
		+ "h1 { font-size: 1.25rem; margin: 0 0 0.5rem; } "
		+ "p { color: #b3b3b3; margin: 0; }";

	private const string CloseScript = "setTimeout(() => window.close(), 1500);";

	// The page runs on the host's public origin, so it may only execute its own inline style and script.
	private static readonly string _contentSecurityPolicy
		= $"default-src 'none'; script-src 'sha256-{Sha256(CloseScript)}'; style-src 'sha256-{Sha256(PageStyle)}'; "
		+ "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

	private readonly IOAuthCallbackCoordinator _coordinator;
	private readonly ILocalizationResolver _localization;
	private readonly IAppPreferenceService _preferences;

	public OAuthCallbackController(
		IOAuthCallbackCoordinator coordinator,
		ILocalizationResolver localization,
		IAppPreferenceService preferences)
	{
		_coordinator = coordinator;
		_localization = localization;
		_preferences = preferences;
	}

	[HttpGet("callback")]
	[AllowAnonymous]
	public async Task<IActionResult> Callback(
		[FromQuery] string? code,
		[FromQuery] string? state,
		[FromQuery] string? error,
		CancellationToken ct)
	{
		var handled = await _coordinator.HandleCallbackAsync(state ?? string.Empty, code, error, ct);

		var (title, message) = (handled, error) switch
		{
			(false, _) => (AppStrings.ConfigFlow.OAuthCallback.UnmatchedTitle(),
				AppStrings.ConfigFlow.OAuthCallback.UnmatchedMessage()),
			(true, not null) => (AppStrings.ConfigFlow.AuthorizationIncomplete(),
				AppStrings.ConfigFlow.OAuthCallback.FailedMessage()),
			_ => (AppStrings.ConfigFlow.OAuthCallback.CompleteTitle(),
				AppStrings.ConfigFlow.OAuthCallback.CompleteMessage())
		};
		var culture = (await _preferences.GetLocalization()).Culture;

		Response.Headers.ContentSecurityPolicy = _contentSecurityPolicy;
		Response.Headers.XContentTypeOptions = "nosniff";
		Response.Headers["Referrer-Policy"] = "no-referrer";

		return Content(Page(culture, _localization.Resolve(title, culture), _localization.Resolve(message, culture)),
			"text/html");
	}

	private static string Page(string culture, string title, string message)
	{
		var lang = WebUtility.HtmlEncode(culture);
		var dir = IsRightToLeft(culture) ? "rtl" : "ltr";
		var encodedTitle = WebUtility.HtmlEncode(title);
		var encodedMessage = WebUtility.HtmlEncode(message);

		return $$"""
				 <!doctype html>
				 <html lang="{{lang}}" dir="{{dir}}">
				 <head>
				 	<meta charset="utf-8">
				 	<title>{{encodedTitle}}</title>
				 	<style>{{PageStyle}}</style>
				 </head>
				 <body>
				 	<div class="card">
				 		<h1>{{encodedTitle}}</h1>
				 		<p>{{encodedMessage}}</p>
				 	</div>
				 	<script>{{CloseScript}}</script>
				 </body>
				 </html>
				 """;
	}

	private static bool IsRightToLeft(string culture)
	{
		try
		{
			return CultureInfo.GetCultureInfo(culture).TextInfo.IsRightToLeft;
		}
		catch (CultureNotFoundException)
		{
			return false;
		}
	}

	private static string Sha256(string text)
	{
		return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}
}
