using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.AdGuardHome;

namespace MacroDeckHost.Integrations.AdGuardHome;

internal sealed class AdGuardHomeClient : IAdGuardHomeClient
{
	public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

	private const string UserAgent = "Macro-Deck-AdGuardHome/1.0";

	private static readonly ConcurrentDictionary<bool, HttpClient> _clients = new();

	private readonly AdGuardHomeConnectionSettings _settings;
	private readonly HttpClient _http;
	private readonly TimeSpan _timeout;
	private readonly AuthenticationHeaderValue? _authorization;

	public AdGuardHomeClient(AdGuardHomeConnectionSettings settings)
		: this(settings, _clients.GetOrAdd(settings.AcceptUntrustedCertificate, CreateClient), RequestTimeout)
	{
	}

	internal AdGuardHomeClient(AdGuardHomeConnectionSettings settings, HttpClient http, TimeSpan timeout)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(http);

		_settings = settings;
		_http = http;
		_timeout = timeout;

		if (!string.IsNullOrEmpty(settings.Username))
		{
			var credentials = Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}");
			_authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));
		}
	}

	public async Task<AdGuardHomeServerStatus> GetStatusAsync(CancellationToken cancellationToken)
	{
		var status = await GetAsync<StatusResponse>("status", cancellationToken);

		if (status.ProtectionEnabled is not { } protection ||
			status.Running is not { } running ||
			string.IsNullOrWhiteSpace(status.Version))
		{
			throw Incompatible("status");
		}

		TimeSpan? pause = status.ProtectionDisabledDuration is { } milliseconds && milliseconds > 0
			? TimeSpan.FromMilliseconds(milliseconds)
			: null;

		return new AdGuardHomeServerStatus(protection, running, status.Version, protection ? null : pause);
	}

	public async Task<AdGuardHomeServerStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
	{
		var stats = await GetAsync<StatsResponse>("stats", cancellationToken);

		if (stats.DnsQueries is not { } queries || stats.BlockedFiltering is not { } blocked)
		{
			throw Incompatible("stats");
		}

		return new AdGuardHomeServerStatistics(queries,
			blocked,
			stats.SafeBrowsing ?? 0,
			stats.SafeSearch ?? 0,
			stats.Parental ?? 0,
			stats.AverageProcessingTime ?? 0);
	}

	public Task SetProtectionAsync(bool enabled, TimeSpan? pause, CancellationToken cancellationToken)
		=> PostAsync("protection",
			new ProtectionRequest
			{
				Enabled = enabled,
				Duration = !enabled && pause is { } length ? (long)length.TotalMilliseconds : null
			},
			cancellationToken);

	public async Task<AdGuardHomeFilteringStatus> GetFilteringStatusAsync(CancellationToken cancellationToken)
	{
		var status = await GetAsync<FilteringStatusResponse>("filtering/status", cancellationToken);

		if (status.Enabled is not { } enabled || status.Interval is not { } interval)
		{
			throw Incompatible("filtering/status");
		}

		return new AdGuardHomeFilteringStatus(enabled, interval);
	}

	public Task SetFilteringAsync(bool enabled, int interval, CancellationToken cancellationToken)
		=> PostAsync("filtering/config", new FilteringConfigRequest { Enabled = enabled, Interval = interval },
			cancellationToken);

	public async Task RefreshFiltersAsync(CancellationToken cancellationToken)
	{
		await PostAsync("filtering/refresh", new FilteringRefreshRequest { Whitelist = false }, cancellationToken);
		await PostAsync("filtering/refresh", new FilteringRefreshRequest { Whitelist = true }, cancellationToken);
	}

	private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
		where T : class
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_settings.ControlUrl, path));
		return await SendAsync(request,
			async (response, token) =>
			{
				try
				{
					return await response.Content.ReadFromJsonAsync<T>(token) ?? throw Incompatible(path);
				}
				catch (JsonException exception)
				{
					throw Incompatible(path, exception);
				}
				catch (NotSupportedException exception)
				{
					throw Incompatible(path, exception);
				}
			},
			cancellationToken);
	}

	private async Task PostAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
	{
		var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
		// AdGuard Home rejects a charset parameter with 415, so the media type is sent bare.
		content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_settings.ControlUrl, path)) { Content = content };

		await SendAsync(request, (_, _) => Task.FromResult(true), cancellationToken);
	}

	private async Task<T> SendAsync<T>(
		HttpRequestMessage request,
		Func<HttpResponseMessage, CancellationToken, Task<T>> read,
		CancellationToken cancellationToken)
	{
		request.Headers.Authorization = _authorization;
		request.Headers.Accept.ParseAdd("application/json");

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_timeout);

		try
		{
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			EnsureSuccess(response, request.RequestUri!.AbsolutePath);
			return await read(response, timeout.Token);
		}
		catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
		{
			throw new AdGuardHomeException(AdGuardHomeConnection.Timeout,
				$"AdGuard Home at {_settings.ControlUrl} did not answer within {_timeout.TotalSeconds:0} s.",
				exception);
		}
		catch (HttpRequestException exception)
		{
			throw new AdGuardHomeException(AdGuardHomeConnection.Unreachable,
				$"Could not reach AdGuard Home at {_settings.ControlUrl}: {exception.Message}",
				exception);
		}
	}

	private static void EnsureSuccess(HttpResponseMessage response, string path)
	{
		var code = (int)response.StatusCode;

		if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			throw new AdGuardHomeException(AdGuardHomeConnection.Unauthorized,
				$"AdGuard Home rejected the credentials (HTTP {code}).");
		}

		if (code is >= 300 and < 400)
		{
			throw new AdGuardHomeException(AdGuardHomeConnection.Redirected,
				$"AdGuard Home redirected {path} (HTTP {code}) to {response.Headers.Location}.");
		}

		if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
		{
			throw Incompatible(path);
		}

		if (code is >= 400 and < 500)
		{
			throw new AdGuardHomeException(AdGuardHomeConnection.Incompatible,
				$"AdGuard Home refused {path} with HTTP {code}.");
		}

		if (!response.IsSuccessStatusCode)
		{
			throw new AdGuardHomeException(AdGuardHomeConnection.Unreachable,
				$"AdGuard Home answered {path} with HTTP {code}.");
		}
	}

	private static AdGuardHomeException Incompatible(string path, Exception? innerException = null)
		=> new(AdGuardHomeConnection.Incompatible,
			$"The response to {path} does not match the AdGuard Home API.",
			innerException);

	private static HttpClient CreateClient(bool acceptUntrustedCertificate)
	{
		var handler = new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			AutomaticDecompression = DecompressionMethods.All,
			ConnectTimeout = RequestTimeout,
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		};

		if (acceptUntrustedCertificate)
		{
#pragma warning disable CA5359 // Opt-in per instance for self-signed certificates on home servers.
			handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
		}

		var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return client;
	}
}
