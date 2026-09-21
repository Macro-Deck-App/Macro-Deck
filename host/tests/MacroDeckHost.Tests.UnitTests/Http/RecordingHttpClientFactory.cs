using System.Collections.Concurrent;
using System.Net;

namespace MacroDeckHost.Tests.UnitTests.Http;

internal sealed record RecordedRequest(Uri Url, IReadOnlyDictionary<string, string> Headers)
{
	public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

	public bool HasMacroDeckHeaders =>
		Headers.Keys.Any(name => name.StartsWith("X-MacroDeck-", StringComparison.OrdinalIgnoreCase));
}

internal sealed class RecordingHttpClientFactory : IHttpClientFactory, IDisposable
{
	private readonly RecordingHandler _handler;
	private readonly HttpClient? _sharedClient;

	public RecordingHttpClientFactory(bool shareClient = false)
	{
		_handler = new RecordingHandler(this);
		_sharedClient = shareClient ? new HttpClient(_handler, disposeHandler: false) : null;
	}

	public byte[] Body { get; set; } = [];

	public int HoldUntilInFlight { get; set; }

	public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

	public HttpClient CreateClient(string name) => _sharedClient ?? new HttpClient(_handler, disposeHandler: false);

	public void Dispose()
	{
		_sharedClient?.Dispose();
		_handler.Dispose();
	}

	private sealed class RecordingHandler : HttpMessageHandler
	{
		private readonly RecordingHttpClientFactory _owner;
		private readonly TaskCompletionSource _allInFlight = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _inFlight;

		public RecordingHandler(RecordingHttpClientFactory owner) => _owner = owner;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			_owner.Requests.Enqueue(new RecordedRequest(request.RequestUri!,
				request.Headers.ToDictionary(header => header.Key,
					header => string.Join(",", header.Value),
					StringComparer.OrdinalIgnoreCase)));

			if (_owner.HoldUntilInFlight > 0)
			{
				if (Interlocked.Increment(ref _inFlight) >= _owner.HoldUntilInFlight)
				{
					_allInFlight.TrySetResult();
				}

				await _allInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
			}

			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_owner.Body) };
		}
	}
}
