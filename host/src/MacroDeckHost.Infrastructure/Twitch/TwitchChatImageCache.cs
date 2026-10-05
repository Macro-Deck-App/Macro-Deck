using System.Text.RegularExpressions;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.Ui.Resources;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Twitch;

public sealed partial class TwitchChatImageCache : ITwitchChatImages, IDisposable
{
	public const string HttpClientName = "twitch-chat-images";

	internal const string AllowedHost = "static-cdn.jtvnw.net";
	internal const int MaxImageBytes = 512 * 1024;
	internal const int MaxEntries = 1000;
	internal const long MaxCachedBytes = 32L * 1024 * 1024;
	internal const int MaxConcurrentFetches = 4;

	internal static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(10);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly IUiResourceStore _resources;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly LinkedList<Entry> _order = new();
	private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, DateTimeOffset> _failures = new(StringComparer.Ordinal);
	private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _fetchSlots = new(MaxConcurrentFetches, MaxConcurrentFetches);
	private readonly CancellationTokenSource _disposing = new();

	private HashSet<string> _pinned = new(StringComparer.Ordinal);
	private long _cachedBytes;
	private int _pinnedCount;
	private long _pinnedBytes;
	private bool _disposed;

	public TwitchChatImageCache(
		IHttpClientFactory httpClientFactory,
		IUiResourceStore resources,
		TimeProvider timeProvider,
		ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, resources, timeProvider, logger)
	{
	}

	internal TwitchChatImageCache(
		HttpMessageHandler handler,
		IUiResourceStore resources,
		TimeProvider timeProvider,
		ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, resources, timeProvider, logger)
	{
	}

	private TwitchChatImageCache(
		HttpClient http,
		bool ownsClient,
		IUiResourceStore resources,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_resources = resources;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<TwitchChatImageCache>();
	}

	public event EventHandler<TwitchChatImage>? Resolved;

	internal int Count
	{
		get
		{
			lock (_sync)
			{
				return _entries.Count;
			}
		}
	}

	internal int InFlightCount
	{
		get
		{
			lock (_sync)
			{
				return _inFlight.Count;
			}
		}
	}

	public UiResource? Find(TwitchChatImage image)
	{
		ArgumentNullException.ThrowIfNull(image);

		lock (_sync)
		{
			if (!_entries.TryGetValue(image.Key, out var node))
			{
				return null;
			}

			_order.Remove(node);
			_order.AddFirst(node);

			return node.Value.Resource;
		}
	}

	public void Request(TwitchChatImage image)
	{
		ArgumentNullException.ThrowIfNull(image);

		var key = image.Key;
		var now = _timeProvider.GetUtcNow();

		lock (_sync)
		{
			if (_disposed ||
				_entries.ContainsKey(key) ||
				_inFlight.Contains(key) ||
				(_failures.TryGetValue(key, out var retryAt) && retryAt > now))
			{
				return;
			}

			if (TryBuildUri(image) is not { } uri)
			{
				RememberFailure(key, now);
				return;
			}

			if (_pinnedCount >= MaxEntries || _pinnedBytes >= MaxCachedBytes)
			{
				return;
			}

			_failures.Remove(key);
			_inFlight.Add(key);
			_ = FetchAsync(image, uri, _disposing.Token);
		}
	}

	public void Pin(IReadOnlyCollection<TwitchChatImage> referenced)
	{
		ArgumentNullException.ThrowIfNull(referenced);

		lock (_sync)
		{
			_pinned = referenced.Select(image => image.Key).ToHashSet(StringComparer.Ordinal);
			_pinnedCount = 0;
			_pinnedBytes = 0;

			foreach (var node in _entries.Values)
			{
				if (_pinned.Contains(node.Value.Key))
				{
					_pinnedCount++;
					_pinnedBytes += node.Value.Bytes;
				}
			}
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
		}

		_disposing.Cancel();
		_disposing.Dispose();
		_fetchSlots.Dispose();

		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	internal static Uri? TryBuildUri(TwitchChatImage image)
	{
		var valid = image.Kind == TwitchChatImageKind.Emote
			? EmoteIdPattern().IsMatch(image.Id)
			: image.Id.Split('/') is [var setId, var versionId] &&
				BadgePartPattern().IsMatch(setId) &&
				BadgePartPattern().IsMatch(versionId);

		return valid && IsAllowed(image.Url) ? new Uri(image.Url) : null;
	}

	internal static bool IsAllowed(string? url)
		=> Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
			uri.Scheme == Uri.UriSchemeHttps &&
			uri.IsDefaultPort &&
			string.IsNullOrEmpty(uri.UserInfo) &&
			string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase);

	// The bytes decide the type, whatever the upstream header claims; SVG is script-capable and refused.
	internal static string? SniffImage(byte[] bytes)
	{
		if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
		{
			return "image/png";
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

	private async Task FetchAsync(TwitchChatImage image, Uri uri, CancellationToken cancellationToken)
	{
		byte[]? bytes = null;
		string? mediaType = null;

		try
		{
			await _fetchSlots.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				bytes = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);
				mediaType = bytes is null ? null : SniffImage(bytes);
			}
			finally
			{
				_fetchSlots.Release();
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
		{
			return;
		}

		if (!Store(image, bytes, mediaType))
		{
			return;
		}

		try
		{
			Resolved?.Invoke(this, image);
		}
#pragma warning disable CA1031 // A subscriber's fault must not surface as an unobserved task exception.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Error(exception, "A Twitch chat image subscriber failed");
		}
	}

	private void RememberFailure(string key, DateTimeOffset now)
	{
		_failures[key] = now + FailureLifetime;

		if (_failures.Count <= MaxEntries)
		{
			return;
		}

		foreach (var expired in _failures.Where(failure => failure.Value <= now).Select(failure => failure.Key).ToList())
		{
			_failures.Remove(expired);
		}

		foreach (var oldest in _failures.OrderBy(failure => failure.Value).Take(_failures.Count - MaxEntries).ToList())
		{
			_failures.Remove(oldest.Key);
		}
	}

	private bool Store(TwitchChatImage image, byte[]? bytes, string? mediaType)
	{
		lock (_sync)
		{
			_inFlight.Remove(image.Key);

			if (_disposed)
			{
				return false;
			}

			if (bytes is null || mediaType is null)
			{
				RememberFailure(image.Key, _timeProvider.GetUtcNow());
				return false;
			}

			if (!MakeRoom(bytes.Length))
			{
				return false;
			}

			var resource = _resources.Register(new UiResourceRegistration
			{
				OwnerId = StreamPlatforms.Twitch.OwnerId,
				Name = image.ResourceName,
				MediaType = mediaType,
				Content = bytes,
			});

			var entry = new Entry(image.Key, resource, bytes.Length);
			_entries[image.Key] = _order.AddFirst(entry);
			_cachedBytes += bytes.Length;

			if (_pinned.Contains(image.Key))
			{
				_pinnedCount++;
				_pinnedBytes += bytes.Length;
			}

			return true;
		}
	}

	private bool MakeRoom(int incoming)
	{
		while (_entries.Count + 1 > MaxEntries || _cachedBytes + incoming > MaxCachedBytes)
		{
			var victim = _order.Last;

			while (victim is not null && _pinned.Contains(victim.Value.Key))
			{
				victim = victim.Previous;
			}

			if (victim is null)
			{
				return false;
			}

			_order.Remove(victim);
			_entries.Remove(victim.Value.Key);
			_cachedBytes -= victim.Value.Bytes;
			_resources.Remove(victim.Value.Resource.ResourceId);
		}

		return true;
	}

	private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode ||
				response.Content.Headers.ContentLength > MaxImageBytes ||
				!IsAllowed(response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString()))
			{
				return null;
			}

			var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			await using (stream.ConfigureAwait(false))
			{
				using var buffer = new MemoryStream();
				var chunk = new byte[81920];
				int read;

				while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
				{
					if (buffer.Length + read > MaxImageBytes)
					{
						return null;
					}

					buffer.Write(chunk, 0, read);
				}

				return buffer.ToArray();
			}
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException ||
			(exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			_logger.Debug(exception, "A Twitch chat image could not be fetched");
			return null;
		}
	}

	[GeneratedRegex(@"\A[A-Za-z0-9_]{1,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex EmoteIdPattern();

	[GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex BadgePartPattern();

	private sealed record Entry(string Key, UiResource Resource, int Bytes);
}
