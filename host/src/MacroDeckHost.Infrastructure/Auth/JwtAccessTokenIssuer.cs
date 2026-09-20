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
	private readonly AccessTokenCutoff _cutoff;
	private readonly DeviceSessionGuard _deviceSessions;

	public JwtAccessTokenIssuer(ISigningKeyProvider signingKeyProvider,
		TimeProvider timeProvider,
		AccessTokenCutoff cutoff,
		DeviceSessionGuard deviceSessions)
	{
		_signingKeyProvider = signingKeyProvider;
		_timeProvider = timeProvider;
		_cutoff = cutoff;
		_deviceSessions = deviceSessions;
	}

	public AccessToken Issue(Guid userId, string username, AuthScope scope, Guid? deviceId)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var expiresAt = now.Add(AuthDefaults.AccessTokenLifetimeFor(scope));
		var claims = new Dictionary<string, object>
		{
			[JwtRegisteredClaimNames.Sub] = userId.ToString(),
			[JwtRegisteredClaimNames.Name] = username,
			[AuthDefaults.ScopeClaim] = AuthDefaults.ScopeClaimValue(scope)
		};

		var issuedAt = _cutoff.IssuedAtFor(now);
		if (deviceId is { } device)
		{
			claims[AuthDefaults.DeviceClaim] = device.ToString();
			issuedAt = _deviceSessions.IssuedAtFor(device, issuedAt);
		}

		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = AuthDefaults.Issuer,
			Audience = AuthDefaults.Audience,
			NotBefore = now,
			Expires = expiresAt,
			// iat has one-second resolution, so a token minted in the same second as a password reset or a
			// device sign-out would be refused for its whole life. nbf and exp stay on the real clock.
			IssuedAt = issuedAt,
			Claims = claims,
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKeyProvider.GetKey()),
				SecurityAlgorithms.HmacSha256)
		};

		return new AccessToken(_tokenHandler.CreateToken(descriptor), expiresAt);
	}
}
