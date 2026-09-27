using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record LoopbackProofRequest(string? Nonce);

public record LoopbackProofResponse(string Proof);

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class LoopbackSessionController(TimeProvider timeProvider) : ControllerBase
{
	private const string DesktopPath = "/admin";

	[HttpGet("loopback-session")]
	public IActionResult Session([FromQuery] string? code, [FromQuery] string? v)
	{
		if (!LoopbackConnection.IsLoopbackTransport(HttpContext))
		{
			return NotFound();
		}

		var session = LoopbackSecret.SessionCookieValue();
		if (session is not null && LoopbackSecret.TryRedeemCode(code, timeProvider.GetUtcNow()))
		{
			Response.Cookies.Append(LoopbackConnection.SessionCookieName(HttpContext),
				session,
				new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
		}

		Response.Headers.CacheControl = "no-store";
		return Redirect(IsVersion(v) ? $"{DesktopPath}?v={Uri.EscapeDataString(v!)}" : DesktopPath);
	}

	[HttpPost("loopback-proof")]
	public ActionResult<LoopbackProofResponse> Proof(LoopbackProofRequest body)
	{
		if (!LoopbackConnection.IsLoopbackTransport(HttpContext))
		{
			return NotFound();
		}

		var proof = body.Nonce is null ? null : LoopbackSecret.Proof(body.Nonce);
		return proof is null ? BadRequest() : new LoopbackProofResponse(proof);
	}

	private static bool IsVersion(string? value)
		=> value is { Length: > 0 and <= 64 } &&
			value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '-');
}
