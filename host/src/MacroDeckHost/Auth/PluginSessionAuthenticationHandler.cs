using System.Security.Claims;
using System.Text.Encodings.Web;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Auth;

public class PluginSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	private static readonly JsonWebTokenHandler _tokenHandler = new();

	private readonly ISigningKeyProvider _signingKeyProvider;
	private readonly IPluginSessionRegistry _sessionRegistry;

	public PluginSessionAuthenticationHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger,
		UrlEncoder encoder,
		ISigningKeyProvider signingKeyProvider,
		IPluginSessionRegistry sessionRegistry)
		: base(options, logger, encoder)
	{
		_signingKeyProvider = signingKeyProvider;
		_sessionRegistry = sessionRegistry;
	}

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var header = Request.Headers[PluginAuthDefaults.AuthorizationHeaderName].ToString();
		var prefix = PluginAuthDefaults.BearerScheme + " ";
		if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			return AuthenticateResult.NoResult();
		}

		var rawToken = header[prefix.Length..].Trim();
		if (string.IsNullOrEmpty(rawToken))
		{
			return AuthenticateResult.NoResult();
		}

		var validationParameters = new TokenValidationParameters
		{
			ValidIssuer = AuthDefaults.Issuer,
			ValidAudience = AuthDefaults.Audience,
			IssuerSigningKeyResolver = (_, _, _, _) => [new SymmetricSecurityKey(_signingKeyProvider.GetKey())],
			ClockSkew = TimeSpan.FromSeconds(30)
		};

		TokenValidationResult result;
		try
		{
			result = await _tokenHandler.ValidateTokenAsync(rawToken, validationParameters);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return AuthenticateResult.Fail("The plugin session token could not be parsed.");
		}

		if (!result.IsValid || result.ClaimsIdentity is not { } identity)
		{
			return AuthenticateResult.Fail(result.Exception ?? new SecurityTokenException("Invalid token."));
		}

		var scope = identity.FindFirst(AuthDefaults.ScopeClaim)?.Value;
		if (!string.Equals(scope, PluginAuthDefaults.PluginScope, StringComparison.Ordinal))
		{
			return AuthenticateResult.Fail("Wrong token scope for a plugin session.");
		}

		var pluginId = identity.FindFirst(PluginClaimTypes.PluginId)?.Value;
		var sessionId = identity.FindFirst(PluginClaimTypes.SessionId)?.Value;
		if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(sessionId))
		{
			return AuthenticateResult.Fail("Missing plugin session claims.");
		}

		var isLive = _sessionRegistry.Snapshot().Any(session =>
			string.Equals(session.SessionId, sessionId, StringComparison.Ordinal) &&
			string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));
		if (!isLive)
		{
			return AuthenticateResult.Fail("The plugin session no longer exists.");
		}

		var principal = new ClaimsPrincipal(new ClaimsIdentity([
				new Claim(AuthDefaults.ScopeClaim, PluginAuthDefaults.PluginScope),
				new Claim(PluginClaimTypes.PluginId, pluginId),
				new Claim(PluginClaimTypes.SessionId, sessionId)
			],
			Scheme.Name));

		return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
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
