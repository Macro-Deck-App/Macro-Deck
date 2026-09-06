using System.Net;

namespace MacroDeckHost.Tests.UnitTests.Http;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
	private readonly HttpStatusCode _status;
	private readonly byte[] _body;
	private readonly string? _contentType;
	private readonly List<(string Name, string Value)> _headers;
	private readonly bool _neverAnswers;

	public FakeHttpMessageHandler(
		byte[]? body = null,
		HttpStatusCode status = HttpStatusCode.OK,
		string? contentType = null,
		IEnumerable<(string Name, string Value)>? headers = null,
		bool neverAnswers = false)
	{
		_body = body ?? [];
		_status = status;
		_contentType = contentType;
		_headers = headers?.ToList() ?? [];
		_neverAnswers = neverAnswers;
	}

	public static FakeHttpMessageHandler NeverAnswers() => new(neverAnswers: true);

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		if (_neverAnswers)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		}

		var response = new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) };

		if (_contentType is not null)
		{
			response.Content.Headers.Remove("Content-Type");
			response.Content.Headers.TryAddWithoutValidation("Content-Type", _contentType);
		}

		foreach (var (name, value) in _headers)
		{
			if (!response.Headers.TryAddWithoutValidation(name, value))
			{
				response.Content.Headers.TryAddWithoutValidation(name, value);
			}
		}

		return response;
	}
}
