using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Store.Reviews;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Announcements;

public sealed class PlatformAnnouncementClient : IPlatformAnnouncementClient, IDisposable
{
	public const string HttpClientName = "platform-announcements";
	public const long MaximumResponseBytes = 1024 * 1024;

	private const string LatestPath = "api/v1/public/announcements/latest";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly Uri _baseUrl;
	private readonly TimeSpan _requestTimeout;
	private readonly ILogger _logger;

	public PlatformAnnouncementClient(IHttpClientFactory httpClientFactory, StorePlatformOptions options, ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, options, logger)
	{
	}

	internal PlatformAnnouncementClient(HttpMessageHandler handler, StorePlatformOptions options, ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, options, logger)
	{
	}

	private PlatformAnnouncementClient(HttpClient http, bool ownsClient, StorePlatformOptions options, ILogger logger)
	{
		_http = http;
		_http.MaxResponseContentBufferSize = MaximumResponseBytes;
		_ownsClient = ownsClient;
		_baseUrl = options.BaseUrl;
		_requestTimeout = options.RequestTimeout;
		_logger = logger.ForContext<PlatformAnnouncementClient>();
	}

	public async Task<AnnouncementFetch> GetLatest(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_requestTimeout);
		try
		{
			using var response = await _http.GetAsync(new Uri(_baseUrl, LatestPath), timeout.Token);
			if (response.StatusCode == HttpStatusCode.NoContent)
			{
				return new AnnouncementFetch.NonePublished();
			}

			if (response.StatusCode != HttpStatusCode.OK)
			{
				_logger.Debug("The Macro Deck Platform answered the latest announcement with {Status}", (int)response.StatusCode);
				return new AnnouncementFetch.Failed();
			}

			var body = await response.Content.ReadFromJsonAsync<LatestResponse>(Json, timeout.Token);
			return body is { Number: > 0, Title: { } title, Content: { } content, PublishedAt: { } publishedAt }
				? new AnnouncementFetch.Published(new Announcement(body.Number,
					title,
					content,
					publishedAt,
					body.UpdatedAt ?? publishedAt))
				: new AnnouncementFetch.Failed();
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested
			&& ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
		{
			_logger.Debug("Fetching the latest announcement failed: {Error}", ex.GetType().Name);
			return new AnnouncementFetch.Failed();
		}
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private sealed record LatestResponse(
		int Number,
		string? Title,
		string? Content,
		DateTimeOffset? PublishedAt,
		DateTimeOffset? UpdatedAt);
}
