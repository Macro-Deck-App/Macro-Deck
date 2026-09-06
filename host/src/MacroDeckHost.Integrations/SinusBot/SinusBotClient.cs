using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MacroDeckHost.Integrations.SinusBot;

internal sealed class SinusBotClient : ISinusBotClient
{
	private const string ApiSegment = "/api/v1";

	private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

	private static readonly HttpClient _http = CreateHttpClient();

	private readonly string _baseUrl;
	private readonly string _apiBotUrl;
	private readonly string _apiV1Url;

	private string? _token;
	private string? _username;
	private string? _password;

	public SinusBotClient(string baseUrl)
	{
		_baseUrl = baseUrl.TrimEnd('/');
		_apiV1Url = $"{_baseUrl}{ApiSegment}";
		_apiBotUrl = $"{_apiV1Url}/bot";
	}

	public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
	{
		_username = username;
		_password = password;
		_token = null;
		return AuthenticateInternalAsync(cancellationToken);
	}

	public Task<IReadOnlyList<SinusBotInstance>> GetInstancesAsync(CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(async ct =>
				(IReadOnlyList<SinusBotInstance>)await GetAsync<List<SinusBotInstance>>($"{_apiBotUrl}/instances", ct),
			cancellationToken);

	public Task<SinusBotInstanceStatus> GetInstanceStatusAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => GetAsync<SinusBotInstanceStatus>(InstanceUrl(instanceId, "status"), ct),
			cancellationToken);

	public Task<IReadOnlyList<SinusBotFile>> GetFilesAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(async ct =>
				(IReadOnlyList<SinusBotFile>)await GetAsync<List<SinusBotFile>>($"{_apiBotUrl}/files", ct),
			cancellationToken);

	public string GetThumbnailUrl(string instanceId, string thumbnailId) => $"{_baseUrl}/cache/{thumbnailId}";

	public Task PlayAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "play"), ct), cancellationToken);

	public Task PauseAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "pause"), ct), cancellationToken);

	public Task StopAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "stop"), ct), cancellationToken);

	public Task PlayFileAsync(string instanceId, string fileId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(
			ct => PostAsync(InstanceUrl(instanceId, $"play/byId/{Uri.EscapeDataString(fileId)}"), ct),
			cancellationToken);

	public Task NextAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "playNext"), ct), cancellationToken);

	public Task PreviousAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "playPrevious"), ct), cancellationToken);

	public Task SeekAsync(string instanceId, int seconds, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, $"seek/{Math.Max(0, seconds)}"), ct),
			cancellationToken);

	public Task SetVolumeAsync(string instanceId, int volumePercent, CancellationToken cancellationToken)
	{
		var clamped = Math.Clamp(volumePercent, 0, 100);
		return ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, $"volume/set/{clamped}"), ct),
			cancellationToken);
	}

	public Task IncreaseVolumeAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "volume/up"), ct), cancellationToken);

	public Task DecreaseVolumeAsync(string instanceId, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, "volume/down"), ct), cancellationToken);

	public Task SetShuffleAsync(string instanceId, bool enabled, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, $"shuffle/{(enabled ? "1" : "0")}"), ct),
			cancellationToken);

	public Task SetRepeatAsync(string instanceId, bool enabled, CancellationToken cancellationToken)
		=> ExecuteWithReauthAsync(ct => PostAsync(InstanceUrl(instanceId, $"repeat/{(enabled ? "1" : "0")}"), ct),
			cancellationToken);

	private async Task AuthenticateInternalAsync(CancellationToken cancellationToken)
	{
		try
		{
			var botId = await GetBotIdAsync(cancellationToken);

			var payload = new LoginRequest
			{
				Username = _username,
				Password = _password,
				BotId = botId
			};

			using var content = JsonContent.Create(payload, options: _jsonOptions);
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBotUrl}/login");
			request.Content = content;
			using var response = await _http.SendAsync(request, cancellationToken);

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				throw new SinusBotAuthException("Wrong SinusBot username or password.");
			}

			if (!response.IsSuccessStatusCode)
			{
				throw new SinusBotAuthException($"SinusBot login failed (HTTP {(int)response.StatusCode}).");
			}

			var login = await ReadJsonAsync<LoginResponse>(response, cancellationToken) ??
				throw new SinusBotAuthException("SinusBot did not return a login response.");

			if (login.Success is false || string.IsNullOrWhiteSpace(login.Token))
			{
				throw new SinusBotAuthException("SinusBot rejected the login. Check username and password.");
			}

			_token = login.Token;
		}
		catch (HttpRequestException ex) when (ex.StatusCode is null)
		{
			throw new SinusBotAuthException($"Could not reach SinusBot at {_baseUrl}.", ex);
		}
		catch (JsonException)
		{
			throw new SinusBotAuthException(
				$"\"{_baseUrl}\" did not return a SinusBot API response. Check that the URL points to your SinusBot (e.g. http://your-bot:8087).");
		}
	}

	private async Task<string> GetBotIdAsync(CancellationToken cancellationToken)
	{
		using var request = CreateRequest(HttpMethod.Get, $"{_apiV1Url}/botId");
		using var response = await _http.SendAsync(request, cancellationToken);
		EnsureSuccess(response);

		var body = await ReadJsonAsync<BotIdResponse>(response, cancellationToken);
		return body?.DefaultBotId ?? throw new SinusBotApiException("SinusBot did not return a bot id.");
	}

	private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
		where T : class
	{
		using var request = CreateRequest(HttpMethod.Get, url);
		using var response = await _http.SendAsync(request, cancellationToken);
		EnsureSuccess(response);

		return await ReadJsonAsync<T>(response, cancellationToken) ??
			throw new SinusBotApiException("SinusBot returned no data.");
	}

	private async Task PostAsync(string url, CancellationToken cancellationToken)
	{
		using var request = CreateRequest(HttpMethod.Post, url);
		using var response = await _http.SendAsync(request, cancellationToken);
		EnsureSuccess(response);
	}

	private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
		where T : class
	{
		var mediaType = response.Content.Headers.ContentType?.MediaType;
		if (!string.IsNullOrEmpty(mediaType) && !mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase))
		{
			throw new SinusBotApiException(
				$"SinusBot returned a non-JSON response ({mediaType}). Check the server URL.");
		}

		try
		{
			return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
		}
		catch (JsonException ex)
		{
			throw new SinusBotApiException("SinusBot returned an unreadable response. Check the server URL.", ex);
		}
	}

	private async Task<T> ExecuteWithReauthAsync<T>(Func<CancellationToken, Task<T>> action,
		CancellationToken cancellationToken)
	{
		try
		{
			return await action(cancellationToken);
		}
		catch (SinusBotApiException ex) when (IsReauthable(ex))
		{
			await AuthenticateInternalAsync(cancellationToken);
			return await action(cancellationToken);
		}
	}

	private async Task ExecuteWithReauthAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (SinusBotApiException ex) when (IsReauthable(ex))
		{
			await AuthenticateInternalAsync(cancellationToken);
			await action(cancellationToken);
		}
	}

	private bool IsReauthable(SinusBotApiException ex)
		=> ex.StatusCode == HttpStatusCode.Unauthorized &&
			!string.IsNullOrEmpty(_username) &&
			!string.IsNullOrEmpty(_password);

	private HttpRequestMessage CreateRequest(HttpMethod method, string url)
	{
		var request = new HttpRequestMessage(method, url);
		if (_token is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
		}

		return request;
	}

	private string InstanceUrl(string instanceId, string action)
		=> $"{_apiBotUrl}/i/{Uri.EscapeDataString(instanceId)}/{action}";

	private static void EnsureSuccess(HttpResponseMessage response)
	{
		if (!response.IsSuccessStatusCode)
		{
			throw new SinusBotApiException(
				$"SinusBot request failed: {(int)response.StatusCode} {response.ReasonPhrase}.",
				response.StatusCode);
		}
	}

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(10)
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("Macro-Deck-SinusBot/1.0");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		return client;
	}
}
