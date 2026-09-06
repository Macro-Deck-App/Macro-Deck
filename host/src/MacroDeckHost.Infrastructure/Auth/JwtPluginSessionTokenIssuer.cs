using MacroDeck.Plugin.Protocol.Auth;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Infrastructure.Auth;

public class JwtPluginSessionTokenIssuer : IPluginSessionTokenIssuer
{
	private static readonly JsonWebTokenHandler _tokenHandler = new();

	private readonly ISigningKeyProvider _signingKeyProvider;
	private readonly TimeProvider _timeProvider;

	public JwtPluginSessionTokenIssuer(ISigningKeyProvider signingKeyProvider, TimeProvider timeProvider)
	{
		_signingKeyProvider = signingKeyProvider;
		_timeProvider = timeProvider;
	}

	public string Issue(string pluginId, string sessionId)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var expiresAt = now.Add(PluginAuthDefaults.SessionTokenLifetime);
		var claims = new Dictionary<string, object>
		{
			[AuthDefaults.ScopeClaim] = PluginAuthDefaults.PluginScope,
			[PluginClaimTypes.PluginId] = pluginId,
			[PluginClaimTypes.SessionId] = sessionId
		};

		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = AuthDefaults.Issuer,
			Audience = AuthDefaults.Audience,
			NotBefore = now,
			Expires = expiresAt,
			IssuedAt = now,
			Claims = claims,
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKeyProvider.GetKey()),
				SecurityAlgorithms.HmacSha256)
		};

		return _tokenHandler.CreateToken(descriptor);
	}
}
