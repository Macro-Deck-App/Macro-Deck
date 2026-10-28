using System.Net;
using System.Text;

namespace MacroDeckHost.Tests.UnitTests.Discord;

internal sealed class FakeDiscordTokenEndpoint : HttpMessageHandler
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

	public FakeDiscordTokenEndpoint Answers(HttpStatusCode status, string body)
	{
		_steps.Add(_ => Task.FromResult(new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		}));
		return this;
	}

	public FakeDiscordTokenEndpoint Hangs()
	{
		_steps.Add(async cancellationToken =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK);
		});
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
}
