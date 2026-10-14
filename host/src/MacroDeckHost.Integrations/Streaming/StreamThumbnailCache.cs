using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations.Streaming;

public interface IStreamThumbnails
{
	event EventHandler<string>? Changed;

	UiResource? Find(string accountId);

	void Track(string accountId, string? url);
}

public sealed class StreamThumbnailCache : IStreamThumbnails, IDisposable
{
	internal const int MaxImageBytes = 512 * 1024;

	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

	private readonly StreamPlatform _platform;
	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly Func<IUiResourceRegistry?> _resources;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly CancellationTokenSource _disposing = new();

	private bool _disposed;

	public StreamThumbnailCache(
		StreamPlatform platform,
		Func<IUiResourceRegistry?> resources,
		TimeProvider timeProvider,
		ILogger logger)
		: this(platform,
			IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15), handler => handler.AllowAutoRedirect = false),
			true,
			resources,
			timeProvider,
			logger)
	{
	}

	internal StreamThumbnailCache(
		StreamPlatform platform,
		HttpClient http,
		Func<IUiResourceRegistry?> resources,
		TimeProvider timeProvider,
		ILogger logger)
		: this(platform, http, true, resources, timeProvider, logger)
	{
	}

	private StreamThumbnailCache(
		StreamPlatform platform,
		HttpClient http,
		bool ownsClient,
		Func<IUiResourceRegistry?> resources,
		TimeProvider timeProvider,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(platform);

		_platform = platform;
		_http = http;
		_ownsClient = ownsClient;
		_resources = resources;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StreamThumbnailCache>();
	}

	public event EventHandler<string>? Changed;

	public UiResource? Find(string accountId)
	{
		lock (_sync)
		{
			return _entries.TryGetValue(accountId, out var entry) ? entry.Resource : null;
		}
	}

	public void Track(string accountId, string? url)
	{
		ArgumentException.ThrowIfNullOrEmpty(accountId);

		if (url is null)
		{
			Drop(accountId);
			return;
		}

		if (_platform.Thumbnails is not { } rule || !IsAllowed(rule, url) || !rule.IsValidAccountId(accountId))
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var known = _entries.TryGetValue(accountId, out var entry);

			if (known && entry!.InFlight)
			{
				return;
			}

			if (known && entry!.Url == url && now - entry.FetchedAt < RefreshInterval)
			{
				return;
			}

			_entries[accountId] = (entry ?? new Entry()) with { Url = url, InFlight = true };
		}

		_ = FetchAsync(accountId, new Uri(url), _disposing.Token);
	}

	public void Clear()
	{
		List<Entry> entries;

		lock (_sync)
		{
			entries = [.. _entries.Values];
			_entries.Clear();
		}

		foreach (var entry in entries.Where(entry => entry.Resource is not null))
		{
			Remove(entry.ResourceName);
		}
	}

	public void Dispose()
	{
		List<Entry> entries;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			entries = [.. _entries.Values];
			_entries.Clear();
		}

		_disposing.Cancel();
		_disposing.Dispose();

		foreach (var entry in entries.Where(entry => entry.Resource is not null))
		{
			Remove(entry.ResourceName);
		}

		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	internal static bool IsAllowed(StreamThumbnailRule rule, string? url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out var uri)
			&& uri.Scheme == Uri.UriSchemeHttps
			&& uri.IsDefaultPort
			&& string.IsNullOrEmpty(uri.UserInfo)
			&& string.Equals(uri.Host, rule.AllowedHost, StringComparison.OrdinalIgnoreCase)
			&& uri.AbsolutePath.StartsWith(rule.AllowedPathPrefix, StringComparison.Ordinal);
	}

	internal static bool IsJpeg(byte[] bytes)
	{
		return bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
	}

	private void Drop(string accountId)
	{
		Entry? removed;

		lock (_sync)
		{
			if (!_entries.Remove(accountId, out removed))
			{
				return;
			}
		}

		if (removed.Resource is not null)
		{
			Remove(removed.ResourceName);
			Changed?.Invoke(this, accountId);
		}
	}

	private async Task FetchAsync(string accountId, Uri uri, CancellationToken cancellationToken)
	{
		var bytes = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);
		var name = ResourceName(accountId);
		UiResource? resource = null;

		if (bytes is not null && IsJpeg(bytes) && _resources() is { } registry)
		{
			resource = await RegisterAsync(registry, name, bytes, cancellationToken).ConfigureAwait(false);
		}

		lock (_sync)
		{
			if (_disposed || !_entries.TryGetValue(accountId, out var entry))
			{
				return;
			}

			if (entry.Url != uri.ToString())
			{
				resource = null;
			}

			_entries[accountId] = entry with
			{
				InFlight = false,
				FetchedAt = _timeProvider.GetUtcNow(),
				Resource = resource ?? entry.Resource,
				ResourceName = name
			};
		}

		if (resource is not null)
		{
			Changed?.Invoke(this, accountId);
		}
	}

	private async Task<UiResource?> RegisterAsync(
		IUiResourceRegistry registry,
		string name,
		byte[] bytes,
		CancellationToken cancellationToken)
	{
		try
		{
			return await registry.RegisterAsync(name, bytes, "image/jpeg", cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is UiResourceException or ArgumentException)
		{
			_logger.Debug(exception, "A stream thumbnail for {Platform} could not be registered", _platform.OwnerId);
			return null;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	private void Remove(string name)
	{
		if (_resources() is { } registry)
		{
			_ = RemoveAsync(registry, name);
		}
	}

	private static async Task RemoveAsync(IUiResourceRegistry registry, string name)
	{
		try
		{
			await registry.RemoveAsync(name).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is UiResourceException or ArgumentException)
		{
		}
	}

	internal static string ResourceName(string accountId)
	{
		return ("thumbnail-" + accountId).Length <= StreamChatImage.MaxResourceNameLength
			? "thumbnail-" + accountId
			: "thumbnail-" + StreamChatImage.ShortHash(accountId);
	}

	private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes)
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
			_logger.Debug(exception, "A stream thumbnail for {Platform} could not be fetched", _platform.OwnerId);
			return null;
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
		{
			return null;
		}
	}

	private sealed record Entry
	{
		public string? Url { get; init; }

		public bool InFlight { get; init; }

		public DateTimeOffset FetchedAt { get; init; }

		public UiResource? Resource { get; init; }

		public string ResourceName { get; init; } = string.Empty;
	}
}
