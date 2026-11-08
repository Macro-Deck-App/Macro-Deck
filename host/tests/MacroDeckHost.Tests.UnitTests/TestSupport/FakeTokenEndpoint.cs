using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class FakeTokenEndpoint : HttpMessageHandler
{
	internal const string FreshTokens =
		"""{"access_token":"fresh-access","refresh_token":"fresh-refresh","expires_in":3600,"scope":"rpc"}""";

	private readonly Lock _lock = new();
	private readonly List<Func<CancellationToken, Task<HttpResponseMessage>>> _steps = [];
	private int _requestCount;

	public int RequestCount
	{
		get
		{
			lock (_lock)
			{
				return _requestCount;
			}
		}
	}

	public FakeTokenEndpoint Answers(HttpStatusCode status, string body)
	{
		_steps.Add(_ => Task.FromResult(Response(status, body)));
		return this;
	}

	public FakeTokenEndpoint Answers(HttpStatusCode status, string body, TimeSpan retryAfter)
	{
		_steps.Add(_ =>
		{
			var response = Response(status, body);
			response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
			return Task.FromResult(response);
		});
		return this;
	}

	public FakeTokenEndpoint Hangs()
	{
		_steps.Add(async cancellationToken =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK);
		});
		return this;
	}

	public FakeTokenEndpoint TimesOut()
	{
		_steps.Add(_ => Task.FromException<HttpResponseMessage>(new TaskCanceledException(
			"The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing.",
			new TimeoutException("The operation was canceled."))));
		return this;
	}

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		Func<CancellationToken, Task<HttpResponseMessage>> step;
		lock (_lock)
		{
			step = _steps[Math.Min(_requestCount, _steps.Count - 1)];
			_requestCount++;
		}

		return step(cancellationToken);
	}

	private static HttpResponseMessage Response(HttpStatusCode status, string body)
	{
		return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}
}
