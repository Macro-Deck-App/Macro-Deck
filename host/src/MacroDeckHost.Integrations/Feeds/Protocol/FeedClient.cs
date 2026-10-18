using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace MacroDeckHost.Integrations.Feeds.Protocol;

internal static class FeedErrorCodes
{
	public const string Timeout = "timeout";

	public const string BlockedAddress = "blocked-address";

	public const string InvalidFeed = "invalid-feed";

	public const string TooLarge = "too-large";

	public const string Network = "network";

	public static string Http(int status)
	{
		return $"http-{status}";
	}
}

internal enum FeedFetchStatus
{
	Ok,
	NotModified,
	Failed
}

internal sealed record FeedRequest(Uri Url, bool AllowLocalNetwork)
{
	public string? ETag { get; init; }

	public string? LastModified { get; init; }

	public long MaxBytes { get; init; } = FeedClient.MaxFeedBytes;

	public string Accept { get; init; } = FeedClient.FeedAccept;
}

internal sealed record FeedFetchResult(FeedFetchStatus Status)
{
	public byte[]? Content { get; init; }

	public Uri? FinalUrl { get; init; }

	public string? ETag { get; init; }

	public string? LastModified { get; init; }

	public string? MediaType { get; init; }

	public string? Error { get; init; }

	public TimeSpan? RetryAfter { get; init; }

	public static FeedFetchResult Failed(string error, TimeSpan? retryAfter = null)
	{
		return new FeedFetchResult(FeedFetchStatus.Failed) { Error = error, RetryAfter = retryAfter };
	}
}

internal interface IFeedFetcher
{
	Task<FeedFetchResult> FetchAsync(FeedRequest request, CancellationToken cancellationToken);
}

internal sealed class FeedClient : IFeedFetcher, IDisposable
{
	public const long MaxFeedBytes = 5 * 1024 * 1024;

	public const int MaxRedirects = 5;

	public const int MaxConcurrentRequests = 4;

	public const string FeedAccept =
		"application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.9, */*;q=0.5";

	public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

	private readonly Func<bool, HttpMessageHandler> _handlerFactory;
	private readonly IFeedAddressResolver _resolver;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _gate = new(MaxConcurrentRequests, MaxConcurrentRequests);
	private readonly Lock _sync = new();

	private HttpClient? _strict;
	private HttpClient? _local;
	private bool _disposed;

	public FeedClient(TimeProvider time)
		: this(FeedHttp.CreateHandler, DnsFeedAddressResolver.Instance, time)
	{
	}

	internal FeedClient(
		Func<bool, HttpMessageHandler> handlerFactory,
		IFeedAddressResolver resolver,
		TimeProvider time)
	{
		_handlerFactory = handlerFactory;
		_resolver = resolver;
		_time = time;
	}

	public async Task<FeedFetchResult> FetchAsync(FeedRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			using var timeout = new CancellationTokenSource(RequestTimeout, _time);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			try
			{
				return await SendAsync(request, linked.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return FeedFetchResult.Failed(FeedErrorCodes.Timeout);
			}
			catch (HttpRequestException exception) when (IsBlocked(exception))
			{
				return FeedFetchResult.Failed(FeedErrorCodes.BlockedAddress);
			}
			catch (Exception exception) when (exception is HttpRequestException or IOException)
			{
				return FeedFetchResult.Failed(FeedErrorCodes.Network);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_strict?.Dispose();
			_local?.Dispose();
		}
	}

	internal static bool IsWebUrl(Uri? url)
	{
		return url is { IsAbsoluteUri: true }
			&& url.Scheme is "http" or "https"
			&& !string.IsNullOrEmpty(url.Host)
			&& string.IsNullOrEmpty(url.UserInfo);
	}

	private async Task<FeedFetchResult> SendAsync(FeedRequest request, CancellationToken cancellationToken)
	{
		var url = request.Url;
		var client = Client(request.AllowLocalNetwork);

		for (var hop = 0;; hop++)
		{
			if (!IsWebUrl(url))
			{
				return FeedFetchResult.Failed(FeedErrorCodes.InvalidFeed);
			}

			if (!await IsReachableAsync(url, request.AllowLocalNetwork, cancellationToken).ConfigureAwait(false))
			{
				return FeedFetchResult.Failed(FeedErrorCodes.BlockedAddress);
			}

			using var message = new HttpRequestMessage(HttpMethod.Get, url);
			message.Headers.TryAddWithoutValidation("Accept", request.Accept);
			if (request.ETag is { Length: > 0 } etag)
			{
				message.Headers.TryAddWithoutValidation("If-None-Match", etag);
			}

			if (request.LastModified is { Length: > 0 } lastModified)
			{
				message.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
			}

			using var response = await client
				.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
			var status = (int)response.StatusCode;

			if (IsRedirect(response.StatusCode))
			{
				if (hop >= MaxRedirects || response.Headers.Location is not { } location)
				{
					return FeedFetchResult.Failed(FeedErrorCodes.Http(status));
				}

				url = location.IsAbsoluteUri ? location : new Uri(url, location);
				continue;
			}

			if (response.StatusCode == HttpStatusCode.NotModified)
			{
				return new FeedFetchResult(FeedFetchStatus.NotModified)
				{
					FinalUrl = url,
					ETag = ETagOf(response) ?? request.ETag,
					LastModified = LastModifiedOf(response) ?? request.LastModified
				};
			}

			if (!response.IsSuccessStatusCode)
			{
				var retryAfter = response.StatusCode is HttpStatusCode.TooManyRequests
					or HttpStatusCode.ServiceUnavailable
					? RetryAfterOf(response.Headers.RetryAfter)
					: null;
				return FeedFetchResult.Failed(FeedErrorCodes.Http(status), retryAfter);
			}

			if (response.Content.Headers.ContentLength > request.MaxBytes)
			{
				return FeedFetchResult.Failed(FeedErrorCodes.TooLarge);
			}

			var content = await ReadAsync(response.Content, request.MaxBytes, cancellationToken).ConfigureAwait(false);
			return content is null
				? FeedFetchResult.Failed(FeedErrorCodes.TooLarge)
				: new FeedFetchResult(FeedFetchStatus.Ok)
				{
					Content = content,
					FinalUrl = url,
					ETag = ETagOf(response),
					LastModified = LastModifiedOf(response),
					MediaType = response.Content.Headers.ContentType?.MediaType
				};
		}
	}

	private async Task<bool> IsReachableAsync(Uri url, bool allowLocalNetwork, CancellationToken cancellationToken)
	{
		if (allowLocalNetwork)
		{
			return true;
		}

		try
		{
			var addresses = await _resolver.ResolveAsync(url.Host, cancellationToken).ConfigureAwait(false);
			return FeedAddressGuard.IsAllowed(addresses, false);
		}
		catch (SocketException)
		{
			throw new HttpRequestException("The feed host could not be resolved.");
		}
	}

	private HttpClient Client(bool allowLocalNetwork)
	{
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (allowLocalNetwork)
			{
				return _local ??= Create(true);
			}

			return _strict ??= Create(false);
		}
	}

	private HttpClient Create(bool allowLocalNetwork)
	{
		return IntegrationHttp.CreateClient(_handlerFactory(allowLocalNetwork), true, Timeout.InfiniteTimeSpan);
	}

	private TimeSpan? RetryAfterOf(RetryConditionHeaderValue? header)
	{
		if (header?.Delta is { } delta)
		{
			return delta;
		}

		if (header?.Date is { } date)
		{
			var wait = date - _time.GetUtcNow();
			return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
		}

		return null;
	}

	private static async Task<byte[]?> ReadAsync(
		HttpContent content,
		long maxBytes,
		CancellationToken cancellationToken)
	{
		var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			using var buffer = new MemoryStream();
			var chunk = new byte[81920];
			int read;

			while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
			{
				if (buffer.Length + read > maxBytes)
				{
					return null;
				}

				buffer.Write(chunk, 0, read);
			}

			return buffer.ToArray();
		}
	}

	private static bool IsRedirect(HttpStatusCode status)
	{
		return status is HttpStatusCode.MovedPermanently
			or HttpStatusCode.Found
			or HttpStatusCode.SeeOther
			or HttpStatusCode.TemporaryRedirect
			or HttpStatusCode.PermanentRedirect;
	}

	private static bool IsBlocked(Exception exception)
	{
		for (var current = exception; current is not null; current = current.InnerException)
		{
			if (current is FeedAddressBlockedException)
			{
				return true;
			}
		}

		return false;
	}

	private static string? ETagOf(HttpResponseMessage response)
	{
		return response.Headers.ETag?.ToString();
	}

	private static string? LastModifiedOf(HttpResponseMessage response)
	{
		return response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);
	}
}
