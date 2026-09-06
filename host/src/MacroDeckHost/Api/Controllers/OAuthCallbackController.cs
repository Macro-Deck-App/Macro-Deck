using MacroDeckHost.Application.Integrations.ConfigFlow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/integrations/oauth")]
public class OAuthCallbackController : ControllerBase
{
	private readonly IOAuthCallbackCoordinator _coordinator;

	public OAuthCallbackController(IOAuthCallbackCoordinator coordinator)
	{
		_coordinator = coordinator;
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
			(false, _) => ("Authorization could not be matched", "This window can be closed."),
			(true, not null) => ("Authorization failed", $"The provider returned: {error}. You can close this window."),
			_ => ("Authorization complete", "You can close this window and return to Macro Deck.")
		};

		return Content(Page(title, message), "text/html");
	}

	private static string Page(string title, string message)
		=> $$"""
			 <!doctype html>
			 <html lang="en">
			 <head>
			 	<meta charset="utf-8">
			 	<title>{{title}}</title>
			 	<style>
			 		body { font-family: system-ui, sans-serif; background: #121212; color: #fff;
			 			display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
			 		.card { text-align: center; padding: 2rem 2.5rem; }
			 		h1 { font-size: 1.25rem; margin: 0 0 0.5rem; }
			 		p { color: #b3b3b3; margin: 0; }
			 	</style>
			 </head>
			 <body>
			 	<div class="card">
			 		<h1>{{title}}</h1>
			 		<p>{{message}}</p>
			 	</div>
			 	<script>setTimeout(() => window.close(), 1500);</script>
			 </body>
			 </html>
			 """;
}
