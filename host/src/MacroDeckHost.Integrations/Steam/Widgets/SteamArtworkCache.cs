using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using Serilog;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed partial class SteamArtworkCache : IDisposable
{
	public const string AllowedHost = "cdn.cloudflare.steamstatic.com";

	internal static readonly IReadOnlyList<string> AvatarHosts =
	[
		"avatars.steamstatic.com",
		"avatars.fastly.steamstatic.com",
		"avatars.cloudflare.steamstatic.com",
		"avatars.akamai.steamstatic.com"
	];

	internal const int MaxImageBytes = 1024 * 1024;

	internal const int MaxEntries = 48;

	internal const long MaxTotalBytes = 24L * 1024 * 1024;

	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

	internal static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

	private static readonly ILogger _logger = IntegrationLog.For<SteamArtworkCache>(SteamIntegration.IntegrationId);

	private readonly HttpClient _http;
	private IUiResourceRegistry _resources;
	private readonly TimeProvider _time;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
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
		return Find(CoverName(appId));
	}

	public UiResource? FindAvatar(string? url)
	{
		return AvatarName(url) is { } name ? Find(name) : null;
	}

	public void Track(int appId)
	{
		if (appId > 0 && UriFor(appId) is { } uri)
		{
			Track(CoverName(appId), uri);
		}
	}

	public void TrackAvatar(string? url)
	{
		if (AvatarName(url) is { } name)
		{
			Track(name, new Uri(url!));
		}
	}

	private UiResource? Find(string name)
	{
		lock (_sync)
		{
			return _entries.TryGetValue(name, out var entry) ? entry.Resource : null;
		}
	}

	private void Track(string name, Uri uri)
	{
		var now = _time.GetUtcNow();
		List<string> evicted;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var known = _entries.TryGetValue(name, out var entry);
			if (known && entry!.InFlight)
			{
				return;
			}

			if (known && now - entry!.FetchedAt < (entry.Resource is null ? RetryInterval : RefreshInterval))
			{
				_entries[name] = entry with { LastUsed = now };
				return;
			}

			_entries[name] = (entry ?? new Entry()) with { InFlight = true, LastUsed = now };
			evicted = Evict(name);
		}

		foreach (var evictedName in evicted)
		{
			_ = RemoveAsync(evictedName);
		}

		_ = FetchAsync(name, uri, _disposing.Token);
	}

	// A registry handed over after the integration was disabled replaces a released one, whose covers
	// are gone, so every cover is fetched again into the new one.
	public void UseResources(IUiResourceRegistry resources)
	{
		lock (_sync)
		{
			if (_disposed || ReferenceEquals(_resources, resources))
			{
				return;
			}

			_resources = resources;
			_entries.Clear();
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Dispose()
	{
		List<string> registered;

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

		foreach (var name in registered)
		{
			_ = RemoveAsync(name);
		}

		_http.Dispose();
	}

	internal static HttpClient CreateClient()
	{
		return IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15), PinToTheCdn);
	}

	// Redirects stay off so a response can never move the request away from the pinned CDN host.
	internal static void PinToTheCdn(SocketsHttpHandler handler)
	{
		handler.AllowAutoRedirect = false;
	}

	internal static Uri? UriFor(int appId)
	{
		var uri = new Uri(
			$"https://{AllowedHost}/steam/apps/{appId.ToString(CultureInfo.InvariantCulture)}/library_600x900.jpg");

		return IsAllowed(uri) ? uri : null;
	}

	internal static bool IsAllowed(Uri uri)
	{
		return uri.Scheme == Uri.UriSchemeHttps
			&& uri.IsDefaultPort
			&& string.IsNullOrEmpty(uri.UserInfo)
			&& string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)
			&& uri.AbsolutePath.StartsWith("/steam/apps/", StringComparison.Ordinal);
	}

	internal static bool IsAllowedAvatar(Uri uri)
	{
		return uri.Scheme == Uri.UriSchemeHttps
			&& uri.IsDefaultPort
			&& string.IsNullOrEmpty(uri.UserInfo)
			&& string.IsNullOrEmpty(uri.Query)
			&& AvatarHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
			&& AvatarPath().IsMatch(uri.AbsolutePath);
	}

	internal static string? AvatarName(string? url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAllowedAvatar(uri)
			? $"avatar-{uri.AbsolutePath[1..41].ToLowerInvariant()}"
			: null;
	}

	internal static bool IsJpeg(byte[] bytes)
	{
		return bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
	}

	private static string CoverName(int appId)
	{
		return $"cover-{appId.ToString(CultureInfo.InvariantCulture)}";
	}

	[GeneratedRegex("^/[0-9a-fA-F]{40}_medium\\.jpg$", RegexOptions.CultureInvariant)]
	private static partial Regex AvatarPath();

	private List<string> Evict(string keep)
	{
		var removed = new List<string>();

		while (_entries.Count > MaxEntries || _entries.Values.Sum(entry => (long)entry.Size) > MaxTotalBytes)
		{
			var victim = _entries.Where(pair => pair.Key != keep && !pair.Value.InFlight)
				.OrderBy(pair => pair.Value.LastUsed)
				.Select(pair => pair.Key)
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

	private async Task RemoveAsync(string name)
	{
		try
		{
			await _resources.RemoveAsync(name);
		}
		catch (Exception exception) when (exception is UiResourceException or OperationCanceledException)
		{
			_logger.Debug(exception, "A Steam cover could not be released");
		}
	}

	private async Task FetchAsync(string name, Uri uri, CancellationToken cancellationToken)
	{
		var bytes = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);

		UiResource? registered = null;
		if (bytes is not null && IsJpeg(bytes))
		{
			try
			{
				registered = await _resources.RegisterAsync(name, bytes, "image/jpeg", cancellationToken)
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
			if (_disposed || !_entries.TryGetValue(name, out var entry))
			{
				orphaned = registered is not null;
			}
			else
			{
				_entries[name] = entry with
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
			await RemoveAsync(name).ConfigureAwait(false);
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
		catch (Exception exception) when (exception is HttpRequestException or IOException
			|| exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
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
