using MacroDeckHost.Application.Auth;
using MacroDeckHost.Domain.Enums;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Infrastructure.Auth;

public class JwtAccessTokenIssuer : IAccessTokenIssuer
{
	private static readonly JsonWebTokenHandler _tokenHandler = new();

	private readonly ISigningKeyProvider _signingKeyProvider;
	private readonly TimeProvider _timeProvider;

	public JwtAccessTokenIssuer(ISigningKeyProvider signingKeyProvider, TimeProvider timeProvider)
	{
		_signingKeyProvider = signingKeyProvider;
		_timeProvider = timeProvider;
	}

	public AccessToken Issue(Guid userId, string username, AuthScope scope, Guid? deviceId)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var expiresAt = now.Add(AuthDefaults.AccessTokenLifetime);
		var claims = new Dictionary<string, object>
		{
			[JwtRegisteredClaimNames.Sub] = userId.ToString(),
			[JwtRegisteredClaimNames.Name] = username,
			[AuthDefaults.ScopeClaim] = AuthDefaults.ScopeClaimValue(scope)
		};

		if (deviceId is { } device)
		{
			claims[AuthDefaults.DeviceClaim] = device.ToString();
		}

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

		return new AccessToken(_tokenHandler.CreateToken(descriptor), expiresAt);
	}
}
