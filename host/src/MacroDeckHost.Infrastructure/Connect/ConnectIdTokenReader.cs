using MacroDeckHost.Application.Connect;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed record ConnectIdTokenClaims(
	string Subject,
	string DisplayName,
	string? PictureUrl,
	string? CreatorUsername,
	IReadOnlyList<string> Roles);

public static class ConnectIdTokenReader
{
	private const string RoleClaim = "role";
	private const string CreatorUsernameClaim = "macrodeck:creator-username";

	private static readonly TimeSpan _clockSkew = TimeSpan.FromMinutes(2);

	// The trust anchor for these claims is the direct TLS channel to the issuer's own token endpoint,
	// which is where every id token read here comes from - never a redirect, never a client-supplied
	// value - and the claims are display-only: nothing in the host authorizes anything off them. That is
	// the only reason the signature is not validated against the issuer's JWKS. The moment any decision
	// (entitlement, role gate, quota) is taken off these claims, full JWKS validation becomes required.
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
			ReadClaim(token, CreatorUsernameClaim),
			[
				.. token.Claims
					.Where(claim => string.Equals(claim.Type, RoleClaim, StringComparison.Ordinal))
					.Select(claim => claim.Value)
			]);
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
