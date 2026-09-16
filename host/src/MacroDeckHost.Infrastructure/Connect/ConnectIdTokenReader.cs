using System.Text.Json;
using MacroDeckHost.Application.Connect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed record ConnectIdTokenClaims(
	string Subject,
	string DisplayName,
	string? PictureUrl,
	string? CreatorUsername);

public static class ConnectIdTokenReader
{
	private const string CreatorUsernameClaim = "preferred_username";
	private const string RolesClaim = "urn:zitadel:iam:org:project:roles";

	private static readonly TimeSpan _clockSkew = TimeSpan.FromMinutes(2);

	// Profile claims are display-only and trusted through the direct TLS channel to the token endpoint.
	// Roles decide something, so they count only once ReadVerifiedRoles checked the signature (ADR 0054).
	public static ConnectIdTokenClaims Read(string idToken, string? expectedNonce, TimeProvider timeProvider)
	{
		JsonWebToken token;
		try
		{
			token = new JsonWebToken(idToken);
		}
		catch (Exception ex)
		{
			throw new ConnectAuthTransientException("Macro Deck Connect returned an unreadable id token.", ex);
		}

		if (!IssuerMatches(token.Issuer))
		{
			throw new ConnectAuthTransientException(
				$"The id token was issued by {token.Issuer} instead of {ConnectEndpoints.Issuer}.");
		}

		if (!token.Audiences.Contains(ConnectEndpoints.ClientId, StringComparer.Ordinal))
		{
			throw new ConnectAuthTransientException("The id token was not issued for this client.");
		}

		if (string.IsNullOrEmpty(token.Subject))
		{
			throw new ConnectAuthTransientException("The id token carries no subject.");
		}

		if (token.ValidTo != DateTime.MinValue &&
			new DateTimeOffset(token.ValidTo, TimeSpan.Zero) + _clockSkew < timeProvider.GetUtcNow())
		{
			throw new ConnectAuthTransientException("The id token has already expired.");
		}

		// A refresh returns an id token without a nonce, so a nonce is only required where one was sent -
		// the code exchange - and there it must match exactly.
		if (expectedNonce is not null &&
			!string.Equals(ReadClaim(token, "nonce"), expectedNonce, StringComparison.Ordinal))
		{
			throw new ConnectAuthRejectedException("The id token does not answer this sign-in attempt.");
		}

		return new ConnectIdTokenClaims(token.Subject,
			ReadClaim(token, "name") ?? token.Subject,
			ReadClaim(token, "picture"),
			ReadClaim(token, CreatorUsernameClaim));
	}

	// Runs between a refresh-token rotation and its durable save, so it must never throw: anything it
	// cannot verify is null, which leaves the previously verified roles in place.
	public static async Task<IReadOnlyList<string>?> ReadVerifiedRoles(string idToken, string? signingKeys)
	{
		if (signingKeys is null)
		{
			return null;
		}

		try
		{
			var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken,
				new TokenValidationParameters
				{
					IssuerSigningKeys = new JsonWebKeySet(signingKeys).GetSigningKeys(),
					ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
					ValidIssuer = ConnectEndpoints.Issuer,
					ValidAudience = ConnectEndpoints.ClientId,
					ValidateLifetime = false
				});

			if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
			{
				return null;
			}

			using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));

			return payload.RootElement.TryGetProperty(RolesClaim, out var roles) &&
				roles.ValueKind is JsonValueKind.Object
					? roles.EnumerateObject().Select(role => role.Name).Order(StringComparer.Ordinal).ToList()
					: [];
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static bool IssuerMatches(string? issuer)
		=> issuer is not null &&
			string.Equals(issuer.TrimEnd('/'),
				ConnectEndpoints.Issuer.TrimEnd('/'),
				StringComparison.OrdinalIgnoreCase);

	private static string? ReadClaim(JsonWebToken token, string type)
	{
		var claim = token.Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.Ordinal));

		return string.IsNullOrEmpty(claim?.Value) ? null : claim.Value;
	}
}
