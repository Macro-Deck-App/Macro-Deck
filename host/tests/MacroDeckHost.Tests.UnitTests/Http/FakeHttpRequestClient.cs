using MacroDeckHost.Integrations.Http.Client;

namespace MacroDeckHost.Tests.UnitTests.Http;

internal sealed class FakeHttpRequestClient : IHttpRequestClient
{
	private readonly Queue<HttpSendOutcome> _outcomes = new();

	public List<HttpRequestSpec> Requests { get; } = [];

	public FakeHttpRequestClient Enqueue(HttpSendOutcome outcome)
	{
		_outcomes.Enqueue(outcome);
		return this;
	}

	public Task<HttpSendOutcome> SendAsync(HttpRequestSpec spec, CancellationToken cancellationToken)
	{
		Requests.Add(spec);

		var outcome = _outcomes.Count > 0
			? _outcomes.Dequeue()
			: HttpSendOutcome.Failed(HttpFailureKind.Unreachable, 0);
		return Task.FromResult(outcome);
	}
}
