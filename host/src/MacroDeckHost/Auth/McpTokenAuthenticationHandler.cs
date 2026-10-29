using System.Security.Claims;
using System.Text.Encodings.Web;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MacroDeckHost.Auth;

public class McpTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	private const string BearerPrefix = "Bearer ";

	private readonly IMcpTokenService _tokenService;

	public McpTokenAuthenticationHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger,
		UrlEncoder encoder,
		IMcpTokenService tokenService)
		: base(options, logger, encoder)
	{
		_tokenService = tokenService;
	}

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var header = Request.Headers.Authorization.ToString();
		if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return AuthenticateResult.NoResult();
		}

		var rawToken = header[BearerPrefix.Length..].Trim();
		if (!rawToken.StartsWith(McpTokenService.TokenPrefix, StringComparison.Ordinal))
		{
			return AuthenticateResult.NoResult();
		}

		var token = await _tokenService.Authenticate(rawToken);
		if (token is null)
		{
			return AuthenticateResult.Fail("Unknown, revoked or expired MCP token.");
		}

		var identity = new ClaimsIdentity([
				new Claim(ClaimTypes.NameIdentifier, "mcp:" + token.Id),
				new Claim(AuthDefaults.ScopeClaim, AuthDefaults.McpScope),
				new Claim(McpClaimTypes.TokenId, token.Id.ToString()),
				new Claim(McpClaimTypes.TokenName, token.Name)
			],
			Scheme.Name);

		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
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
