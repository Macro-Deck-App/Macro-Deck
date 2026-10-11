using System.Globalization;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Steam;
using MacroDeckHost.Application.Ui.Resources;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.Steam;

public sealed class SteamArtworkCache : IDisposable
{
	public const string AllowedHost = "cdn.cloudflare.steamstatic.com";

	internal const int MaxImageBytes = 1024 * 1024;

	internal const int MaxEntries = 48;

	internal const long MaxTotalBytes = 24L * 1024 * 1024;

	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

	internal static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

	private readonly HttpClient _http;
	private readonly IUiResourceStore _resources;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly Dictionary<int, Entry> _entries = [];
	private readonly CancellationTokenSource _disposing = new();

	private bool _disposed;

	public SteamArtworkCache(
		IHttpUserAgentSource userAgent,
		IUiResourceStore resources,
		TimeProvider time,
		ILogger logger)
		: this(CreateHandler(userAgent), resources, time, logger)
	{
	}

	internal SteamArtworkCache(HttpMessageHandler handler, IUiResourceStore resources, TimeProvider time,
		ILogger logger)
	{
		_http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };
		_resources = resources;
		_time = time;
		_logger = logger.ForContext<SteamArtworkCache>();
	}

	public event EventHandler? Changed;

	public UiResource? Find(int appId)
	{
		lock (_sync)
		{
			return _entries.TryGetValue(appId, out var entry) ? entry.Resource : null;
		}
	}

	public void Track(int appId)
	{
		if (appId <= 0)
		{
			return;
		}

		var now = _time.GetUtcNow();
		List<UiResource> evicted;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var known = _entries.TryGetValue(appId, out var entry);
			if (known && entry!.InFlight)
			{
				return;
			}

			if (known && now - entry!.FetchedAt < (entry.Resource is null ? RetryInterval : RefreshInterval))
			{
				_entries[appId] = entry with { LastUsed = now };
				return;
			}

			_entries[appId] = (entry ?? new Entry()) with { InFlight = true, LastUsed = now };
			evicted = Evict(appId);
		}

		foreach (var resource in evicted)
		{
			_resources.Remove(resource.ResourceId);
		}

		_ = FetchAsync(appId, _disposing.Token);
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
			_resources.Remove(entry.Resource!.ResourceId);
		}

		_http.Dispose();
	}

	internal static HttpMessageHandler CreateHandler(IHttpUserAgentSource userAgent)
	{
		// Redirects stay off so a response can never move the request away from the pinned CDN host.
		return new UserAgentHandler(userAgent, new SocketsHttpHandler { AllowAutoRedirect = false });
	}

	internal static Uri? UriFor(int appId)
	{
		var uri = new Uri(
			$"https://{AllowedHost}/steam/apps/{appId.ToString(CultureInfo.InvariantCulture)}/library_600x900.jpg");

		return IsAllowed(uri) ? uri : null;
	}

	internal static bool IsAllowed(Uri uri)
		=> uri.Scheme == Uri.UriSchemeHttps &&
			uri.IsDefaultPort &&
			string.IsNullOrEmpty(uri.UserInfo) &&
			string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase) &&
			uri.AbsolutePath.StartsWith("/steam/apps/", StringComparison.Ordinal);

	internal static bool IsJpeg(byte[] bytes)
		=> bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

	private List<UiResource> Evict(int keep)
	{
		var removed = new List<UiResource>();

		while (_entries.Count > MaxEntries || _entries.Values.Sum(entry => (long)entry.Size) > MaxTotalBytes)
		{
			var victim = _entries.Where(pair => pair.Key != keep && !pair.Value.InFlight)
				.OrderBy(pair => pair.Value.LastUsed)
				.Select(pair => pair.Key)
				.Cast<int?>()
				.FirstOrDefault();

			if (victim is not { } evictedId)
			{
				break;
			}

			if (_entries.Remove(evictedId, out var entry) && entry.Resource is not null)
			{
				removed.Add(entry.Resource);
			}
		}

		return removed;
	}

	private async Task FetchAsync(int appId, CancellationToken cancellationToken)
	{
		byte[]? bytes = null;
		if (UriFor(appId) is { } uri)
		{
			bytes = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);
		}

		UiResource? replaced = null;
		UiResource? registered = null;

		lock (_sync)
		{
			if (_disposed || !_entries.TryGetValue(appId, out var entry))
			{
				return;
			}

			if (bytes is not null && IsJpeg(bytes))
			{
				registered = _resources.Register(new UiResourceRegistration
				{
					OwnerId = SteamWidgetType.OwnerId,
					Name = $"cover-{appId.ToString(CultureInfo.InvariantCulture)}",
					MediaType = "image/jpeg",
					Content = bytes,
				});
				replaced = entry.Resource;
			}

			_entries[appId] = entry with
			{
				InFlight = false,
				FetchedAt = _time.GetUtcNow(),
				Resource = registered ?? entry.Resource,
				Size = registered is not null ? bytes!.Length : entry.Size
			};
		}

		if (replaced is not null && replaced.ResourceId != registered?.ResourceId)
		{
			_resources.Remove(replaced.ResourceId);
		}

		if (registered is not null)
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
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
		catch (Exception exception) when (exception is HttpRequestException or IOException ||
			(exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			_logger.Debug(exception, "Steam artwork could not be fetched");
			return null;
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
		{
			return null;
		}
	}

	private sealed record Entry
	{
		public bool InFlight { get; init; }

		public DateTimeOffset FetchedAt { get; init; }

		public DateTimeOffset LastUsed { get; init; }

		public UiResource? Resource { get; init; }

		public int Size { get; init; }
	}
}
