using System.Globalization;
using System.Security.Claims;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Auth;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class AccessTokenCutoffTests
{
	private static readonly DateTime ResetAt = new(2026, 9, 13, 12, 0, 0, 500, DateTimeKind.Utc);

	[Test]
	public void Nothing_is_rejected_before_any_reset()
	{
		var cutoff = new AccessTokenCutoff();

		Assert.That(cutoff.Rejects(IssuedAt(ResetAt.AddMinutes(-10))), Is.False);
	}

	[Test]
	public void Tokens_issued_before_or_within_the_reset_second_are_rejected_and_later_ones_accepted()
	{
		var cutoff = new AccessTokenCutoff();
		cutoff.Set(ResetAt);

		Assert.Multiple(() =>
		{
			Assert.That(cutoff.Rejects(IssuedAt(ResetAt.AddMinutes(-10))), Is.True);
			Assert.That(cutoff.Rejects(IssuedAt(ResetAt)), Is.True);
			Assert.That(cutoff.Rejects(IssuedAt(ResetAt.AddSeconds(1))), Is.False);
		});
	}

	[Test]
	public void A_principal_without_an_issue_time_is_never_rejected()
	{
		var cutoff = new AccessTokenCutoff();
		cutoff.Set(ResetAt);

		Assert.That(cutoff.Rejects(new ClaimsPrincipal(new ClaimsIdentity([], "Loopback"))), Is.False);
	}

	[Test]
	public void A_token_issued_in_the_reset_second_is_accepted_and_valid_at_once()
	{
		var cutoff = new AccessTokenCutoff();
		cutoff.Set(ResetAt);
		var issuer = new JwtAccessTokenIssuer(new FixedKey(), new FixedTime(ResetAt), cutoff);

		var token = new JsonWebToken(issuer.Issue(Guid.NewGuid(), "admin", AuthScope.Client, null).Token);

		Assert.Multiple(() =>
		{
			Assert.That(cutoff.Rejects(new ClaimsPrincipal(new ClaimsIdentity(token.Claims, "test"))), Is.False);
			Assert.That(token.ValidFrom, Is.LessThanOrEqualTo(ResetAt));
		});
	}

	private static ClaimsPrincipal IssuedAt(DateTime at)
		=> new(new ClaimsIdentity([
				new Claim("iat", new DateTimeOffset(at).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
			],
			"test"));

	private sealed class FixedKey : ISigningKeyProvider
	{
		public byte[] GetKey() => Enumerable.Repeat((byte)7, 64).ToArray();
	}

	private sealed class FixedTime(DateTime utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => new(utcNow);
	}
}
