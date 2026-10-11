using System.Globalization;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Http;
using Serilog;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed class SteamArtworkCache : IDisposable
{
	public const string AllowedHost = "cdn.cloudflare.steamstatic.com";

	internal const int MaxImageBytes = 1024 * 1024;

	internal const int MaxEntries = 48;

	internal const long MaxTotalBytes = 24L * 1024 * 1024;

	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

	internal static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

	private static readonly ILogger _logger = IntegrationLog.For<SteamArtworkCache>(SteamIntegration.IntegrationId);

	private readonly HttpClient _http;
	private readonly IUiResourceRegistry _resources;
	private readonly TimeProvider _time;
	private readonly Lock _sync = new();
	private readonly Dictionary<int, Entry> _entries = [];
	private readonly CancellationTokenSource _disposing = new();

	private bool _disposed;

	public SteamArtworkCache(IUiResourceRegistry resources, TimeProvider time)
		: this(CreateClient(), resources, time)
	{
	}

	internal SteamArtworkCache(HttpClient http, IUiResourceRegistry resources, TimeProvider time)
	{
		_http = http;
		_resources = resources;
		_time = time;
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
		List<int> evicted;

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

		foreach (var evictedAppId in evicted)
		{
			_ = RemoveAsync(evictedAppId);
		}

		_ = FetchAsync(appId, _disposing.Token);
	}

	public void Dispose()
	{
		List<int> registered;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			registered = [.. _entries.Where(pair => pair.Value.Resource is not null).Select(pair => pair.Key)];
			_entries.Clear();
		}

		_disposing.Cancel();
		_disposing.Dispose();

		foreach (var appId in registered)
		{
			_ = RemoveAsync(appId);
		}

		_http.Dispose();
	}

	internal static HttpClient CreateClient()
		=> IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15), PinToTheCdn);

	// Redirects stay off so a response can never move the request away from the pinned CDN host.
	internal static void PinToTheCdn(SocketsHttpHandler handler) => handler.AllowAutoRedirect = false;

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

	private static string NameOf(int appId) => $"cover-{appId.ToString(CultureInfo.InvariantCulture)}";

	private List<int> Evict(int keep)
	{
		var removed = new List<int>();

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
				removed.Add(evictedId);
			}
		}

		return removed;
	}

	private async Task RemoveAsync(int appId)
	{
		try
		{
			await _resources.RemoveAsync(NameOf(appId));
		}
		catch (Exception exception) when (exception is UiResourceException or OperationCanceledException)
		{
			_logger.Debug(exception, "A Steam cover could not be released");
		}
	}

	private async Task FetchAsync(int appId, CancellationToken cancellationToken)
	{
		byte[]? bytes = null;
		if (UriFor(appId) is { } uri)
		{
			bytes = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);
		}

		UiResource? registered = null;
		if (bytes is not null && IsJpeg(bytes))
		{
			try
			{
				registered = await _resources.RegisterAsync(NameOf(appId), bytes, "image/jpeg", cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is UiResourceException or OperationCanceledException)
			{
				_logger.Debug(exception, "A Steam cover could not be registered");
			}
		}

		var orphaned = false;

		lock (_sync)
		{
			if (_disposed || !_entries.TryGetValue(appId, out var entry))
			{
				orphaned = registered is not null;
			}
			else
			{
				_entries[appId] = entry with
				{
					InFlight = false,
					FetchedAt = _time.GetUtcNow(),
					Resource = registered ?? entry.Resource,
					Size = registered is not null ? bytes!.Length : entry.Size
				};
			}
		}

		if (orphaned)
		{
			await RemoveAsync(appId).ConfigureAwait(false);
			return;
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
