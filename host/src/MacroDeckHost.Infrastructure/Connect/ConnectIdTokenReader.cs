using MacroDeckHost.Application.Connect;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed record ConnectIdTokenClaims(
	string Subject,
	string DisplayName,
	string? PictureUrl,
	string? CreatorUsername);

public static class ConnectIdTokenReader
{
	private const string CreatorUsernameClaim = "preferred_username";

	private static readonly TimeSpan _clockSkew = TimeSpan.FromMinutes(2);

	// Display-only claims trusted through the direct TLS channel to the token endpoint, so no JWKS check.
	// Any decision taken off them (role gate, entitlement) makes JWKS validation required (ADR 0054).
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
