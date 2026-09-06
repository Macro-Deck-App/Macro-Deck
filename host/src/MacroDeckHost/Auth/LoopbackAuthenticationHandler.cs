using System.Security.Claims;
using System.Text.Encodings.Web;
using MacroDeckHost.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MacroDeckHost.Auth;

public class LoopbackAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	public LoopbackAuthenticationHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger,
		UrlEncoder encoder)
		: base(options, logger, encoder)
	{
	}

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (!LoopbackConnection.IsTrusted(Context))
		{
			return Task.FromResult(AuthenticateResult.NoResult());
		}

		var identity = new ClaimsIdentity([
				new Claim(ClaimTypes.NameIdentifier, "loopback"),
				new Claim(ClaimTypes.Name, "desktop"),
				new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope)
			],
			Scheme.Name);
		var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

		return Task.FromResult(AuthenticateResult.Success(ticket));
	}

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status401Unauthorized;
		return Task.CompletedTask;
	}

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		return Task.CompletedTask;
	}
}
