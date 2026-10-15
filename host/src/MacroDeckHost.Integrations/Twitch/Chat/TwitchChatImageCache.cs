using System.Text.RegularExpressions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Streaming;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations.Twitch.Chat;

public sealed partial class TwitchChatImageCache : IStreamChatImages, IDisposable
{
	internal const string AllowedHost = "static-cdn.jtvnw.net";
	internal const int MaxImageBytes = 512 * 1024;
	internal const int MaxEntries = 960;
	internal const long MaxCachedBytes = 32L * 1024 * 1024;
	internal const int MaxConcurrentFetches = 4;

	internal static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(10);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly Func<IUiResourceRegistry?> _resources;
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
	private int _reservedCount;
	private long _reservedBytes;
	private long _cachedBytes;
	private int _pinnedCount;
	private long _pinnedBytes;
	private bool _disposed;

	public TwitchChatImageCache(
		Func<IUiResourceRegistry?> resources,
		TimeProvider timeProvider,
		ILogger logger)
		: this(IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15), handler => handler.AllowAutoRedirect = false),
			true,
			resources,
			timeProvider,
			logger)
	{
	}

	internal TwitchChatImageCache(
		HttpClient http,
		Func<IUiResourceRegistry?> resources,
		TimeProvider timeProvider,
		ILogger logger)
		: this(http, true, resources, timeProvider, logger)
	{
	}

	private TwitchChatImageCache(
		HttpClient http,
		bool ownsClient,
		Func<IUiResourceRegistry?> resources,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_resources = resources;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<TwitchChatImageCache>();
	}

	public event EventHandler<StreamChatImage>? Resolved;

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

	public UiResource? Find(StreamChatImage image)
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

	public void Request(StreamChatImage image)
	{
		ArgumentNullException.ThrowIfNull(image);

		var key = image.Key;
		var now = _timeProvider.GetUtcNow();

		lock (_sync)
		{
			if (_disposed
				|| _entries.ContainsKey(key)
				|| _inFlight.Contains(key)
				|| _failures.TryGetValue(key, out var retryAt) && retryAt > now)
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

	public void Pin(IReadOnlyCollection<StreamChatImage> referenced)
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

	public void Clear()
	{
		List<string> names;

		lock (_sync)
		{
			names = [.. _entries.Values.Select(node => node.Value.ResourceName)];
			_entries.Clear();
			_order.Clear();
			_failures.Clear();
			_cachedBytes = 0;
			_pinnedCount = 0;
			_pinnedBytes = 0;
		}

		Remove(names);
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

	internal static Uri? TryBuildUri(StreamChatImage image)
	{
		if (image.Kind == StreamChatImageKind.Emote)
		{
			return EmoteIdPattern().IsMatch(image.Id) ? new Uri(EmoteUrl(image.Id)) : null;
		}

		var valid = image.Id.Split('/') is [var setId, var versionId]
			&& BadgePartPattern().IsMatch(setId)
			&& BadgePartPattern().IsMatch(versionId);

		return valid && IsAllowed(image.Url) ? new Uri(image.Url!) : null;
	}

	internal static string EmoteUrl(string emoteId)
	{
		return $"https://static-cdn.jtvnw.net/emoticons/v2/{emoteId}/default/dark/2.0";
	}

	internal static bool IsAllowed(string? url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out var uri)
			&& uri.Scheme == Uri.UriSchemeHttps
			&& uri.IsDefaultPort
			&& string.IsNullOrEmpty(uri.UserInfo)
			&& string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase);
	}

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

		return bytes.Length >= 12
			&& bytes[0] == 'R'
			&& bytes[1] == 'I'
			&& bytes[2] == 'F'
			&& bytes[3] == 'F'
			&& bytes[8] == 'W'
			&& bytes[9] == 'E'
			&& bytes[10] == 'B'
			&& bytes[11] == 'P'
				? "image/webp"
				: null;
	}

	private async Task FetchAsync(StreamChatImage image, Uri uri, CancellationToken cancellationToken)
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
			Forget(image.Key);
			return;
		}

		if (!await StoreAsync(image, bytes, mediaType, cancellationToken).ConfigureAwait(false))
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

	private void Forget(string key)
	{
		lock (_sync)
		{
			_inFlight.Remove(key);
		}
	}

	private void RememberFailure(string key, DateTimeOffset now)
	{
		_failures[key] = now + FailureLifetime;

		if (_failures.Count <= MaxEntries)
		{
			return;
		}

		foreach (var expired in _failures.Where(failure => failure.Value <= now)
			.Select(failure => failure.Key)
			.ToList())
		{
			_failures.Remove(expired);
		}

		foreach (var oldest in _failures.OrderBy(failure => failure.Value).Take(_failures.Count - MaxEntries).ToList())
		{
			_failures.Remove(oldest.Key);
		}
	}

	// Room is made and reserved before the registration, so concurrent fetches together never take the
	// integration past its resource quota.
	private async Task<bool> StoreAsync(
		StreamChatImage image,
		byte[]? bytes,
		string? mediaType,
		CancellationToken cancellationToken)
	{
		List<string> evicted;

		lock (_sync)
		{
			if (_disposed)
			{
				_inFlight.Remove(image.Key);
				return false;
			}

			if (bytes is null || mediaType is null)
			{
				_inFlight.Remove(image.Key);
				RememberFailure(image.Key, _timeProvider.GetUtcNow());
				return false;
			}

			evicted = [];
			if (!MakeRoom(bytes.Length, evicted))
			{
				_inFlight.Remove(image.Key);
				Remove(evicted);
				return false;
			}

			_reservedCount++;
			_reservedBytes += bytes.Length;
		}

		var registry = _resources();
		UiResource? resource = null;

		try
		{
			if (registry is not null)
			{
				await RemoveAsync(registry, evicted).ConfigureAwait(false);
				resource = await registry.RegisterAsync(image.ResourceName, bytes, mediaType, cancellationToken)
					.ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is UiResourceException
			or ArgumentException
			or OperationCanceledException)
		{
			_logger.Debug(exception, "A Twitch chat image could not be registered");
		}

		lock (_sync)
		{
			_reservedCount--;
			_reservedBytes -= bytes.Length;
			_inFlight.Remove(image.Key);

			if (_disposed || resource is null)
			{
				return false;
			}

			var entry = new Entry(image.Key, image.ResourceName, resource, bytes.Length);
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

	private bool MakeRoom(int incoming, List<string> evicted)
	{
		while (_entries.Count + _reservedCount + 1 > MaxEntries
			|| _cachedBytes + _reservedBytes + incoming > MaxCachedBytes)
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
			evicted.Add(victim.Value.ResourceName);
		}

		return true;
	}

	private void Remove(List<string> names)
	{
		if (names.Count > 0 && _resources() is { } registry)
		{
			_ = RemoveAsync(registry, names);
		}
	}

	private static async Task RemoveAsync(IUiResourceRegistry registry, List<string> names)
	{
		foreach (var name in names)
		{
			try
			{
				await registry.RemoveAsync(name).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is UiResourceException or ArgumentException)
			{
			}
		}
	}

	private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode
				|| response.Content.Headers.ContentLength > MaxImageBytes
				|| !IsAllowed(response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString()))
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
		catch (Exception exception) when (exception is HttpRequestException or IOException
			|| exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
		{
			_logger.Debug(exception, "A Twitch chat image could not be fetched");
			return null;
		}
	}

	[GeneratedRegex(@"\A[A-Za-z0-9_]{1,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex EmoteIdPattern();

	[GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex BadgePartPattern();

	private sealed record Entry(string Key, string ResourceName, UiResource Resource, int Bytes);
}
