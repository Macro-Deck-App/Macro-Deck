using MacroDeckHost.Integrations.Streamerbot.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotAuthenticationTests
{
	private const string Salt = "c2FsdA==";
	private const string Challenge = "Y2hhbGxlbmdl";

	[Test]
	public void CreateResponse_matches_the_documented_vector()
	{
		var response = StreamerbotAuthentication.CreateResponse("streamer", Salt, Challenge);

		Assert.That(response, Is.EqualTo("AFIXJSAwz+e4zaC0apEHiiQ3YXU6wgdXV7CGJXdUF1s="));
	}

	[Test]
	public void CreateResponse_is_bound_to_the_challenge()
	{
		var first = StreamerbotAuthentication.CreateResponse("streamer", Salt, Challenge);
		var second = StreamerbotAuthentication.CreateResponse("streamer", Salt, "b3RoZXI=");

		Assert.That(first, Is.Not.EqualTo(second));
	}

	[Test]
	public void CreateResponse_is_bound_to_the_salt()
	{
		var first = StreamerbotAuthentication.CreateResponse("streamer", Salt, Challenge);
		var second = StreamerbotAuthentication.CreateResponse("streamer", "b3RoZXI=", Challenge);

		Assert.That(first, Is.Not.EqualTo(second));
	}

	[Test]
	public void CreateResponse_never_contains_the_password()
	{
		var response = StreamerbotAuthentication.CreateResponse("hunter2", Salt, Challenge);

		Assert.That(response, Does.Not.Contain("hunter2"));
	}
}
