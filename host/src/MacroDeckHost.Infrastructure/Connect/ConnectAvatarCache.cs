using System.Net;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Paths;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed partial class ConnectAvatarCache : IConnectAvatarCache, IDisposable
{
	private readonly IConnectSessionService _sessionService;
	private readonly IMacroDeckPaths _paths;
	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public ConnectAvatarCache(
		IConnectSessionService sessionService,
		IMacroDeckPaths paths,
		IHttpClientFactory httpClientFactory,
		ILogger logger)
		: this(sessionService,
			paths,
			httpClientFactory.CreateClient(ConnectIdentityClient.HttpClientName),
			false,
			logger)
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
		_paths = paths;
		_http = http;
		_ownsClient = ownsClient;
		_logger = logger.ForContext<ConnectAvatarCache>();
	}

	public async Task<Stream?> GetAvatar(CancellationToken cancellationToken = default)
	{
		var pictureUrl = _sessionService.Current.Account?.PictureUrl;
		if (pictureUrl is null || AvatarIdRegex().Match(pictureUrl) is not { Success: true } match)
		{
			return null;
		}

		var avatarId = match.Groups[1].Value;
		var directory = Path.Combine(_paths.DataRootDirectory, "connect");
		var cached = Path.Combine(directory, $"avatar-{avatarId}.png");

		if (File.Exists(cached))
		{
			return File.OpenRead(cached);
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (File.Exists(cached))
			{
				return File.OpenRead(cached);
			}

			byte[] bytes;
			try
			{
				using var response = await _http.GetAsync(pictureUrl, cancellationToken);
				if (response.StatusCode is HttpStatusCode.NotFound)
				{
					return null;
				}

				response.EnsureSuccessStatusCode();
				bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
			}
			catch (HttpRequestException ex)
			{
				_logger.Debug(ex, "The Macro Deck Connect avatar could not be fetched");
				return null;
			}

			Directory.CreateDirectory(directory);

			// The avatar URL is immutable, so a new picture means a new id: invalidation is simply dropping
			// every previously cached file.
			foreach (var stale in Directory.EnumerateFiles(directory, "avatar-*.png"))
			{
				TryDelete(stale);
			}

			await File.WriteAllBytesAsync(cached, bytes, cancellationToken);

			return new MemoryStream(bytes, writable: false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		_gate.Dispose();

		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

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

	[GeneratedRegex(@"^https://accounts\.macro-deck\.app/avatars/([0-9a-f]{32})\.png$")]
	private static partial Regex AvatarIdRegex();
}
