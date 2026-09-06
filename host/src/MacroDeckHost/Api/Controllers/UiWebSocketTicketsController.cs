using System.Text.Json;
using MacroDeckHost.Auth;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/ui-websocket/tickets")]
[Authorize(Policy = AuthPolicies.ClientAccess)]
public sealed class UiWebSocketTicketsController(IUiWebSocketTickets tickets) : ControllerBase
{
	[HttpPost]
	public IActionResult Create([FromBody] JsonElement request)
	{
		if (!string.Equals(Request.Headers["X-MacroDeck-Ui-Protocol"], "1", StringComparison.Ordinal) ||
			request.ValueKind != JsonValueKind.Object ||
			request.EnumerateObject().Any())
		{
			return BadRequest();
		}

		if (!tickets.TryCreate(HttpContext, User, out var ticket))
		{
			return Forbid();
		}

		Response.Headers.CacheControl = "no-store";
		return Ok(ticket);
	}
}
