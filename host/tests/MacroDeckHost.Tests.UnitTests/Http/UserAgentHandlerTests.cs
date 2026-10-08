using MacroDeckHost.Application.Network.Http;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class UserAgentHandlerTests
{
	private sealed class CapturingHandler : HttpMessageHandler
	{
		public List<string?> UserAgents { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			UserAgents.Add(request.Headers.TryGetValues("User-Agent", out var values)
				? string.Join(" ", values)
				: null);
			return Task.FromResult(new HttpResponseMessage());
		}
	}

	[Test]
	public async Task A_request_without_a_user_agent_carries_the_default()
	{
		var inner = new CapturingHandler();
		using var client = new HttpClient(new UserAgentHandler(new HttpUserAgentState(), inner));

		await client.GetAsync("http://example.invalid/");

		Assert.That(inner.UserAgents, Is.EqualTo(new[] { HttpUserAgent.Default }));
	}

	[Test]
	public async Task A_change_reaches_the_next_request_of_the_same_client()
	{
		var state = new HttpUserAgentState();
		var inner = new CapturingHandler();
		using var client = new HttpClient(new UserAgentHandler(state, inner));

		await client.GetAsync("http://example.invalid/");
		state.Apply("Custom/2.0");
		await client.GetAsync("http://example.invalid/");
		state.Apply(null);
		await client.GetAsync("http://example.invalid/");

		Assert.That(inner.UserAgents,
			Is.EqualTo(new[] { HttpUserAgent.Default, "Custom/2.0", HttpUserAgent.Default }));
	}

	[Test]
	public async Task A_user_agent_set_by_the_caller_is_kept()
	{
		var state = new HttpUserAgentState();
		state.Apply("Custom/2.0");
		var inner = new CapturingHandler();
		using var client = new HttpClient(new UserAgentHandler(state, inner));
		using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.invalid/");
		request.Headers.UserAgent.ParseAdd("Required-By-Api/7");

		await client.SendAsync(request);

		Assert.That(inner.UserAgents, Is.EqualTo(new[] { "Required-By-Api/7" }));
	}

	[Test]
	public async Task A_default_header_of_the_client_counts_as_set_by_the_caller()
	{
		var inner = new CapturingHandler();
		using var client = new HttpClient(new UserAgentHandler(new HttpUserAgentState(), inner));
		client.DefaultRequestHeaders.UserAgent.ParseAdd("Required-By-Api/7");

		await client.GetAsync("http://example.invalid/");

		Assert.That(inner.UserAgents, Is.EqualTo(new[] { "Required-By-Api/7" }));
	}
}
