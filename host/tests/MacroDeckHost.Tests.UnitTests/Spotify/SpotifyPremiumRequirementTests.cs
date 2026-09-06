using System.Net;
using MacroDeckHost.Integrations.Spotify;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyPremiumRequirementTests
{
	[Test]
	public void The_premium_reason_code_is_recognised()
	{
		var ex = Forbidden(
			"""{"error":{"status":403,"message":"Player command failed: Premium required","reason":"PREMIUM_REQUIRED"}}""");

		Assert.That(SpotifyPremiumRequirement.IsRefusal(ex), Is.True);
	}

	[Test]
	public void The_premium_message_without_a_reason_code_is_enough()
	{
		var ex = Forbidden("""{"error":{"status":403,"message":"Player command failed: Premium required"}}""");

		Assert.That(SpotifyPremiumRequirement.IsRefusal(ex), Is.True);
	}

	[Test]
	public void A_wrapped_premium_refusal_is_recognised()
	{
		var inner = Forbidden("""{"error":{"status":403,"message":"Player command failed: Premium required"}}""");

		Assert.That(SpotifyPremiumRequirement.IsRefusal(new InvalidOperationException("wrapped", inner)), Is.True);
	}

	[Test]
	public void A_quota_refusal_is_not_a_premium_refusal()
	{
		var ex = Forbidden(
			"""{"error":{"status":403,"message":"Check settings on developer.spotify.com/dashboard"}}""");

		Assert.Multiple(() =>
		{
			Assert.That(SpotifyPremiumRequirement.IsRefusal(ex), Is.False);
			Assert.That(SpotifyApiLimits.IsQuotaExceeded(ex), Is.True);
		});
	}

	[Test]
	public void A_missing_device_is_not_a_premium_refusal()
	{
		var ex = new APIException(new Response(new Dictionary<string, string>())
		{
			StatusCode = HttpStatusCode.NotFound,
			ContentType = "application/json",
			Body
				= """{"error":{"status":404,"message":"Player command failed: No active device found","reason":"NO_ACTIVE_DEVICE"}}"""
		});

		Assert.That(SpotifyPremiumRequirement.IsRefusal(ex), Is.False);
	}

	private static APIException Forbidden(string body)
		=> new(new Response(new Dictionary<string, string>())
		{
			StatusCode = HttpStatusCode.Forbidden,
			ContentType = "application/json",
			Body = body
		});
}
