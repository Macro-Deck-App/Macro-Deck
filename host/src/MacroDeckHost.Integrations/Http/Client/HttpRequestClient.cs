using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Client;

internal sealed class HttpRequestClient : IHttpRequestClient
{
	private const int ReadChunkBytes = 8 * 1024;

	private static readonly ILogger _logger = IntegrationLog.For<HttpRequestClient>(HttpIntegration.IntegrationId);

	private readonly Func<bool, bool, HttpClient> _clientFactory;

	public HttpRequestClient()
		: this(HttpSharedClients.Get)
	{
	}

	internal HttpRequestClient(Func<bool, bool, HttpClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public async Task<HttpSendOutcome> SendAsync(HttpRequestSpec spec, CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();

		var buildResult = HttpRequestFactory.CreateMessage(spec);
		if (buildResult.Failure is { } buildFailure)
		{
			return HttpSendOutcome.Failed(buildFailure, stopwatch.ElapsedMilliseconds);
		}

		using var message = buildResult.Message!;
		var client = _clientFactory(spec.FollowRedirects, spec.ValidateTls);

		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linkedCts.CancelAfter(spec.Timeout);

		try
		{
			using var response = await client
				.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
				.ConfigureAwait(false);

			var snapshot = await ReadResponseAsync(response, spec.MaxResponseBytes, stopwatch, linkedCts.Token)
				.ConfigureAwait(false);

			var target = $"{spec.Uri.Scheme}://{spec.Uri.Host}{spec.Uri.AbsolutePath}";
			_logger.Debug("HTTP {Method} {Target} answered {Status} in {Duration} ms",
				spec.Method,
				target,
				snapshot.StatusCode,
				snapshot.DurationMs);

			return HttpSendOutcome.Succeeded(snapshot);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return HttpSendOutcome.Failed(HttpFailureKind.Timeout, stopwatch.ElapsedMilliseconds);
		}
		catch (HttpRequestException ex)
		{
			return HttpSendOutcome.Failed(ClassifyHttpRequestException(ex), stopwatch.ElapsedMilliseconds);
		}
		catch (IOException)
		{
			return HttpSendOutcome.Failed(HttpFailureKind.Unreachable, stopwatch.ElapsedMilliseconds);
		}
	}

	private static async Task<HttpResponseSnapshot> ReadResponseAsync(
		HttpResponseMessage response,
		long maxResponseBytes,
		Stopwatch stopwatch,
		CancellationToken cancellationToken)
	{
		var headers = MergeHeaders(response);
		var contentType = response.Content.Headers.ContentType?.MediaType;
		var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);

		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using var buffer = new MemoryStream();
		var chunk = new byte[ReadChunkBytes];
		var truncated = false;

		int read;
		while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
		{
			var remaining = maxResponseBytes - buffer.Length;
			if (remaining <= 0)
			{
				truncated = true;
				break;
			}

			var toWrite = (int)Math.Min(read, remaining);
			buffer.Write(chunk, 0, toWrite);
			if (toWrite < read)
			{
				truncated = true;
				break;
			}
		}

		var body = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);

		return new HttpResponseSnapshot((int)response.StatusCode,
			headers,
			body,
			truncated,
			contentType,
			stopwatch.ElapsedMilliseconds);
	}

	private static Dictionary<string, string> MergeHeaders(HttpResponseMessage response)
	{
		var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		MergeInto(merged, response.Headers);
		MergeInto(merged, response.Content.Headers);
		return merged;
	}

	private static void MergeInto(Dictionary<string, string> target, HttpHeaders headers)
	{
		foreach (var header in headers)
		{
			var value = string.Join(", ", header.Value);
			target[header.Key] = target.TryGetValue(header.Key, out var existing) ? $"{existing}, {value}" : value;
		}
	}

	private static Encoding ResolveEncoding(string? charset)
	{
		if (string.IsNullOrWhiteSpace(charset))
		{
			return Encoding.UTF8;
		}

		try
		{
			return Encoding.GetEncoding(charset.Trim('"'));
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
		{
			return Encoding.UTF8;
		}
	}

	private static HttpFailureKind ClassifyHttpRequestException(HttpRequestException ex)
	{
		for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
		{
			switch (inner)
			{
				case AuthenticationException:
					return HttpFailureKind.TlsRejected;
				case SocketException:
					return HttpFailureKind.Unreachable;
			}
		}

		return HttpFailureKind.Unreachable;
	}
}
