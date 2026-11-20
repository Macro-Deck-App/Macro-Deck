using System.Text.Json;
using MacroDeckHost.Auth;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/ui-websocket/tickets")]
[Authorize(Policy = AuthPolicies.ClientAccess)]
public sealed class UiWebSocketTicketsController(IUiWebSocketTickets tickets) : ControllerBase
{
	private static readonly ILogger _logger = Log.ForContext<UiWebSocketTicketsController>();

	[HttpPost]
	public IActionResult Create([FromBody] JsonElement request)
	{
		if (!string.Equals(Request.Headers["X-MacroDeck-Ui-Protocol"], "1", StringComparison.Ordinal)
			|| request.ValueKind != JsonValueKind.Object
			|| request.EnumerateObject().Any())
		{
			_logger.Warning("Refused a UI WebSocket ticket on port {Port}: the protocol header or the body is invalid",
				HttpContext.Connection.LocalPort);
			return BadRequest();
		}

		if (!tickets.TryCreate(HttpContext, User, out var ticket))
		{
			_logger.Warning(
				"Refused a UI WebSocket ticket on port {Port}: the connection is not classified or has too many open tickets",
				HttpContext.Connection.LocalPort);
			return Forbid();
		}

		Response.Headers.CacheControl = "no-store";
		return Ok(ticket);
	}
}
