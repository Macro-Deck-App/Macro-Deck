using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store.Reviews;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreReviewAvatarProxy : IStoreReviewAvatarProxy, IDisposable
{
	public const string HttpClientName = "store-review-avatar";

	internal const int MaxAvatarBytes = 1024 * 1024;
	internal const int MaxEntries = 200;
	internal const long MaxCachedBytes = 16L * 1024 * 1024;

	private static readonly TimeSpan _lifetime = TimeSpan.FromHours(1);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly LinkedList<(string Url, CachedAvatar Avatar)> _order = new();
	private readonly Dictionary<string, LinkedListNode<(string Url, CachedAvatar Avatar)>> _entries = new(StringComparer.Ordinal);
	private long _cachedBytes;

	public StoreReviewAvatarProxy(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, timeProvider, logger)
	{
	}

	internal StoreReviewAvatarProxy(HttpMessageHandler handler, TimeProvider timeProvider, ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, timeProvider, logger)
	{
	}

	private StoreReviewAvatarProxy(HttpClient http, bool ownsClient, TimeProvider timeProvider, ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StoreReviewAvatarProxy>();
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	public async Task<StoreReviewAvatar?> Fetch(string? sourceUrl, CancellationToken cancellationToken)
	{
		if (!ConnectAssetUrls.IsTrusted(sourceUrl))
		{
			return null;
		}

		var url = sourceUrl!;
		var now = _timeProvider.GetUtcNow();
		lock (_sync)
		{
			if (_entries.TryGetValue(url, out var node) && node.Value.Avatar.ExpiresAt > now)
			{
				_order.Remove(node);
				_order.AddFirst(node);
				return node.Value.Avatar.Image;
			}
		}

		var image = await Download(new Uri(url), cancellationToken);
		if (image is not null)
		{
			Remember(url, new CachedAvatar(image, now + _lifetime));
		}

		return image;
	}

	private async Task<StoreReviewAvatar?> Download(Uri uri, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxAvatarBytes)
			{
				return null;
			}

			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using var buffer = new MemoryStream();
			var chunk = new byte[81920];
			int read;
			while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
			{
				if (buffer.Length + read > MaxAvatarBytes)
				{
					return null;
				}

				buffer.Write(chunk, 0, read);
			}

			var bytes = buffer.ToArray();
			return SniffRaster(bytes) is { } contentType ? new StoreReviewAvatar(bytes, contentType) : null;
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException &&
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Debug(ex, "A Store review avatar could not be fetched");
			return null;
		}
	}

	private void Remember(string url, CachedAvatar avatar)
	{
		lock (_sync)
		{
			if (_entries.Remove(url, out var existing))
			{
				_order.Remove(existing);
				_cachedBytes -= existing.Value.Avatar.Image.Content.Length;
			}

			_entries[url] = _order.AddFirst((url, avatar));
			_cachedBytes += avatar.Image.Content.Length;

			while (_order.Count > MaxEntries || _cachedBytes > MaxCachedBytes)
			{
				var last = _order.Last!;
				_order.RemoveLast();
				_entries.Remove(last.Value.Url);
				_cachedBytes -= last.Value.Avatar.Image.Content.Length;
			}
		}
	}

	// The bytes decide the type: SVG is script-capable and is refused, whatever the upstream header says.
	internal static string? SniffRaster(byte[] bytes)
	{
		if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
		{
			return "image/png";
		}

		if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
		{
			return "image/jpeg";
		}

		if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8')
		{
			return "image/gif";
		}

		return bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
			bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P'
				? "image/webp"
				: null;
	}

	private sealed record CachedAvatar(StoreReviewAvatar Image, DateTimeOffset ExpiresAt);
}
