using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using MacroDeck.Sdk.Scripts;

namespace MacroDeckHost.Integrations.Delegation.Protocol;

internal sealed class DelegateClient : IDelegateClient
{
	private const string UserAgent = "Macro-Deck-Delegate/1.0";

	private static readonly Lazy<HttpClient> _shared = new(CreateClient);

	private readonly HttpClient _http;
	private readonly TimeProvider _time;

	public DelegateClient()
		: this(_shared.Value, TimeProvider.System)
	{
	}

	internal DelegateClient(HttpClient http, TimeProvider? time = null)
	{
		_http = http;
		_time = time ?? TimeProvider.System;
	}

	public async Task ProbeAsync(Uri baseUrl, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(HttpMethod.Get,
			baseUrl,
			"api/system/build-info",
			null,
			null,
			cancellationToken);
		await EnsureIsMacroDeckAsync(response, cancellationToken);
	}

	public async Task<DelegateLoginResult> LoginAsync(Uri baseUrl,
		string username,
		string password,
		CancellationToken cancellationToken)
	{
		var payload = JsonContent.Create(new
			{
				username,
				password,
				scope = "admin"
			},
			options: DelegateJson.Options);

		using var response =
			await SendAsync(HttpMethod.Post, baseUrl, "api/auth/login", null, payload, cancellationToken);
		await ThrowOnFailureAsync(response, cancellationToken);
		var dto = await ReadJsonAsync<DelegateLoginResponseDto>(response, cancellationToken);
		return new DelegateLoginResult(dto.AccessToken, TimeSpan.FromSeconds(dto.ExpiresInSeconds));
	}

	public async Task<DelegateConnectionInfo> GetConnectionInfoAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken)
	{
		using var response = await SendAsync(HttpMethod.Get,
			baseUrl,
			"api/system/connection-info",
			token,
			null,
			cancellationToken);
		await ThrowOnFailureAsync(response, cancellationToken);
		var dto = await ReadJsonAsync<DelegateConnectionInfoDto>(response, cancellationToken);
		return new DelegateConnectionInfo(dto.InstanceName);
	}

	public async Task<IReadOnlyList<DelegateScriptSummary>> GetScriptsAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken)
	{
		using var response =
			await SendAsync(HttpMethod.Get, baseUrl, "api/scripts", token, null, cancellationToken);
		await ThrowOnFailureAsync(response, cancellationToken);

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

		var scripts = new List<DelegateScriptSummary>();
		if (document.RootElement.TryGetProperty("scripts", out var array) && array.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in array.EnumerateArray())
			{
				var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
				var name = item.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
				if (!string.IsNullOrEmpty(id))
				{
					var runsOnWidget = item.TryGetProperty("runsOnWidget", out var runsOnWidgetProperty) &&
						runsOnWidgetProperty.ValueKind == JsonValueKind.True;
					scripts.Add(new DelegateScriptSummary(id,
						string.IsNullOrEmpty(name) ? id : name,
						ReadInputs(item),
						runsOnWidget));
				}
			}
		}

		return scripts;
	}

	public async Task<DelegateRunResult> RunScriptAsync(Uri baseUrl,
		string token,
		string scriptId,
		string? clientId,
		int callDepth,
		IReadOnlyDictionary<string, object?>? inputs,
		CancellationToken cancellationToken)
	{
		var payload = JsonContent.Create(new
			{
				id = scriptId,
				clientId,
				callDepth,
				inputs
			},
			options: DelegateJson.Options);

		using var response = await SendAsync(HttpMethod.Post,
			baseUrl,
			$"api/scripts/{Uri.EscapeDataString(scriptId)}/run",
			token,
			payload,
			cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			var dto = await ReadJsonAsync<DelegateRunResponseDto>(response, cancellationToken);
			if (string.Equals(dto.Error?.Code, "SCRIPT_DEPTH_EXCEEDED", StringComparison.Ordinal))
			{
				throw new DelegateDepthExceededException();
			}

			var error = dto.Error is { Message.Length: > 0 } withMessage
				? withMessage.Message
				: dto.Error?.Code;
			return new DelegateRunResult(dto.Success, error, dto.Status, dto.AppliedInputs);
		}

		await ThrowOnFailureAsync(response, cancellationToken);

		throw new DelegateServerErrorException();
	}

	private static List<ScriptInput> ReadInputs(JsonElement script)
	{
		if (!script.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		try
		{
			return inputs.Deserialize<List<ScriptInput>>(DelegateJson.Options) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private async Task<HttpResponseMessage> SendAsync(
		HttpMethod method,
		Uri baseUrl,
		string path,
		string? token,
		HttpContent? content,
		CancellationToken cancellationToken)
	{
		var uri = new Uri(baseUrl, path);
		using var request = new HttpRequestMessage(method, uri) { Content = content };
		if (token is { Length: > 0 })
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		try
		{
			return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new DelegateUnreachableException("The request timed out.");
		}
		catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
		{
			throw new DelegateTlsException(inner: ex);
		}
		catch (HttpRequestException ex)
		{
			throw new DelegateUnreachableException(inner: ex);
		}
	}

	private static async Task EnsureIsMacroDeckAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (!response.IsSuccessStatusCode)
		{
			throw new DelegateNotMacroDeckException();
		}

		try
		{
			await ReadJsonAsync<DelegateBuildInfoDto>(response, cancellationToken);
		}
		catch (JsonException)
		{
			throw new DelegateNotMacroDeckException();
		}
	}

	private async Task ThrowOnFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		throw response.StatusCode switch
		{
			HttpStatusCode.Unauthorized => new DelegateUnauthorizedException(),
			HttpStatusCode.Forbidden => new DelegateForbiddenException(),
			HttpStatusCode.NotFound => new DelegateNotFoundException(),
			HttpStatusCode.TooManyRequests => new DelegateRateLimitedException(ReadRetryAfter(response)),
			_ => new DelegateServerErrorException($"Unexpected status {(int)response.StatusCode}.")
		};
	}

	private TimeSpan ReadRetryAfter(HttpResponseMessage response)
	{
		var header = response.Headers.RetryAfter;
		if (header?.Delta is { } delta)
		{
			return delta;
		}

		if (header?.Date is { } date)
		{
			return date - _time.GetUtcNow();
		}

		return TimeSpan.FromSeconds(30);
	}

	private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		var result = await response.Content.ReadFromJsonAsync<T>(DelegateJson.Options, cancellationToken);
		return result ?? throw new DelegateServerErrorException("The response could not be understood.");
	}

	private static HttpClient CreateClient()
	{
		var handler = new SocketsHttpHandler
		{
			ConnectTimeout = TimeSpan.FromSeconds(10),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			UseCookies = false
		};

		var client = new HttpClient(handler)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return client;
	}

	public void Dispose()
	{
	}
}
