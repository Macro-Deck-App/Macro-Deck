using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Paths;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed partial class ConnectAvatarCache : IConnectAvatarCache, IDisposable
{
	public const string HttpClientName = "macrodeck-connect-avatar";

	internal const int MaxAvatarBytes = 1024 * 1024;

	private static readonly Uri _issuer = new(ConnectEndpoints.Issuer);

	// SVG is refused on purpose: the host serves the file from its own origin.
	private static readonly Dictionary<string, string> _extensions = new(StringComparer.OrdinalIgnoreCase)
	{
		["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/gif"] = ".gif", ["image/webp"] = ".webp"
	};

	private readonly IConnectSessionService _sessionService;
	private readonly string _directory;
	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _sync = new();

	private Dictionary<string, CachedAvatar>? _entries;

	public ConnectAvatarCache(
		IConnectSessionService sessionService,
		IMacroDeckPaths paths,
		IHttpClientFactory httpClientFactory,
		ILogger logger)
		: this(sessionService, paths, httpClientFactory.CreateClient(HttpClientName), false, logger)
	{
	}

	internal ConnectAvatarCache(
		IConnectSessionService sessionService,
		IMacroDeckPaths paths,
		HttpMessageHandler handler,
		ILogger logger)
		: this(sessionService, paths, new HttpClient(handler, disposeHandler: false), true, logger)
	{
	}

	private ConnectAvatarCache(
		IConnectSessionService sessionService,
		IMacroDeckPaths paths,
		HttpClient http,
		bool ownsClient,
		ILogger logger)
	{
		_sessionService = sessionService;
		_directory = Path.Combine(paths.DataRootDirectory, "connect");
		_http = http;
		_ownsClient = ownsClient;
		_logger = logger.ForContext<ConnectAvatarCache>();
	}

	public event EventHandler? VersionChanged;

	public string? Version => Find(TrustedPictureUrl())?.Version;

	public async Task<ConnectAvatar?> GetAvatar(CancellationToken cancellationToken = default)
	{
		var pictureUrl = TrustedPictureUrl();
		if (pictureUrl is null)
		{
			return null;
		}

		var entry = Find(pictureUrl);
		if (entry is null)
		{
			await Refetch(pictureUrl, cancellationToken);
			entry = Find(pictureUrl);
		}

		if (entry is null)
		{
			return null;
		}

		try
		{
			return new ConnectAvatar(File.OpenRead(entry.Path), entry.ContentType);
		}
		catch (IOException ex)
		{
			_logger.Debug(ex, "The cached Macro Deck Connect avatar could not be opened");
			return null;
		}
	}

	public Task Revalidate(CancellationToken cancellationToken = default)
	{
		if (_sessionService.Current.Account is not { } account)
		{
			return Task.CompletedTask;
		}

		return Refetch(IsTrusted(account.PictureUrl) ? account.PictureUrl : null, cancellationToken);
	}

	public void Dispose()
	{
		_gate.Dispose();

		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private async Task Refetch(string? pictureUrl, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		bool changed;
		try
		{
			var before = Find(pictureUrl)?.Version;

			if (pictureUrl is null)
			{
				DeleteAllExcept(null);
			}
			else
			{
				await Download(pictureUrl, cancellationToken);
			}

			changed = !string.Equals(before, Find(pictureUrl)?.Version, StringComparison.Ordinal);
		}
		finally
		{
			_gate.Release();
		}

		if (changed)
		{
			VersionChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private async Task Download(string pictureUrl, CancellationToken cancellationToken)
	{
		var uri = new Uri(pictureUrl);
		var urlHash = Hash(Encoding.UTF8.GetBytes(pictureUrl));

		try
		{
			using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			if (response.StatusCode is HttpStatusCode.NotFound)
			{
				Forget(urlHash);
				DeleteAllExcept(null);
				return;
			}

			// A followed redirect would hand the host bytes from somewhere other than the issuer's assets.
			if (!response.IsSuccessStatusCode ||
				response.RequestMessage?.RequestUri is { } final && final != uri ||
				response.Content.Headers.ContentType?.MediaType is not { } mediaType ||
				!_extensions.TryGetValue(mediaType, out var extension) ||
				response.Content.Headers.ContentLength > MaxAvatarBytes)
			{
				_logger.Debug("The Macro Deck Connect avatar answered {Status}; keeping the cached copy",
					(int)response.StatusCode);
				return;
			}

			var bytes = await ReadCapped(response.Content, cancellationToken);
			if (bytes is null)
			{
				return;
			}

			Store(urlHash, bytes, extension, mediaType);
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException &&
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Debug(ex, "The Macro Deck Connect avatar could not be fetched; keeping the cached copy");
		}
	}

	private static async Task<byte[]?> ReadCapped(HttpContent content, CancellationToken cancellationToken)
	{
		await using var stream = await content.ReadAsStreamAsync(cancellationToken);
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

		return buffer.ToArray();
	}

	private void Store(string urlHash, byte[] bytes, string extension, string contentType)
	{
		var version = Hash(bytes);
		var path = Path.Combine(_directory, $"avatar-{urlHash}-{version}{extension}");

		if (!File.Exists(path))
		{
			Directory.CreateDirectory(_directory);
			var temp = Path.Combine(_directory, $"tmp-{Guid.NewGuid():N}");
			File.WriteAllBytes(temp, bytes);

			try
			{
				File.Move(temp, path);
			}
			catch (IOException)
			{
				TryDelete(temp);

				if (!File.Exists(path))
				{
					throw;
				}
			}
		}

		lock (_sync)
		{
			Entries()[urlHash] = new CachedAvatar(path, version, contentType);
		}

		DeleteAllExcept(path);
	}

	private CachedAvatar? Find(string? pictureUrl)
	{
		if (pictureUrl is null)
		{
			return null;
		}

		var urlHash = Hash(Encoding.UTF8.GetBytes(pictureUrl));

		lock (_sync)
		{
			return Entries().GetValueOrDefault(urlHash) is { } entry && File.Exists(entry.Path) ? entry : null;
		}
	}

	private void Forget(string urlHash)
	{
		lock (_sync)
		{
			Entries().Remove(urlHash);
		}
	}

	private Dictionary<string, CachedAvatar> Entries()
	{
		if (_entries is not null)
		{
			return _entries;
		}

		_entries = new Dictionary<string, CachedAvatar>(StringComparer.Ordinal);

		if (!Directory.Exists(_directory))
		{
			return _entries;
		}

		var files = Directory.EnumerateFiles(_directory, "avatar-*")
			.Select(path => (Path: path, Match: CachedFileRegex().Match(Path.GetFileName(path))))
			.Where(file => file.Match.Success)
			.OrderBy(file => File.GetLastWriteTimeUtc(file.Path));

		foreach (var (path, match) in files)
		{
			var extension = "." + match.Groups[3].Value;
			var contentType = _extensions.First(pair => pair.Value == extension).Key;
			_entries[match.Groups[1].Value] = new CachedAvatar(path, match.Groups[2].Value, contentType);
		}

		return _entries;
	}

	private void DeleteAllExcept(string? keep)
	{
		if (!Directory.Exists(_directory))
		{
			return;
		}

		foreach (var file in Directory.EnumerateFiles(_directory))
		{
			var name = Path.GetFileName(file);
			if (file != keep &&
				(name.StartsWith("avatar-", StringComparison.Ordinal) ||
					name.StartsWith("tmp-", StringComparison.Ordinal)))
			{
				TryDelete(file);
			}
		}

		lock (_sync)
		{
			foreach (var stale in Entries().Where(pair => pair.Value.Path != keep).Select(pair => pair.Key).ToList())
			{
				Entries().Remove(stale);
			}
		}
	}

	private string? TrustedPictureUrl()
		=> _sessionService.Current.Account?.PictureUrl is { } url && IsTrusted(url) ? url : null;

	// The host fetches with its own network position, so only the issuer's public asset route is allowed.
	private static bool IsTrusted(string? pictureUrl)
		=> Uri.TryCreate(pictureUrl, UriKind.Absolute, out var uri) &&
			uri.Scheme == Uri.UriSchemeHttps &&
			string.Equals(uri.Host, _issuer.Host, StringComparison.OrdinalIgnoreCase) &&
			uri.IsDefaultPort &&
			uri.UserInfo.Length == 0 &&
			uri.AbsolutePath.StartsWith("/assets/", StringComparison.Ordinal);

	private static string Hash(byte[] value)
		=> Convert.ToHexStringLower(SHA256.HashData(value))[..16];

	private void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException ex)
		{
			_logger.Debug(ex, "A stale Macro Deck Connect avatar could not be deleted");
		}
	}

	[GeneratedRegex(@"^avatar-([0-9a-f]{16})-([0-9a-f]{16})\.(png|jpg|gif|webp)$")]
	private static partial Regex CachedFileRegex();

	private sealed record CachedAvatar(string Path, string Version, string ContentType);
}
