using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal sealed class YtmDesktopApiClient : IYtmDesktopApiClient
{
	private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly YtmDesktopEndpoint _endpoint;
	private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _send;

	private string? _token;
	private bool _disposed;

	public YtmDesktopApiClient(YtmDesktopEndpoint endpoint)
		: this(endpoint, send: null)
	{
	}

	internal YtmDesktopApiClient(
		YtmDesktopEndpoint endpoint,
		Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send)
	{
		_endpoint = endpoint;
		_send = send;
	}

	public void UseToken(string? token) => _token = token;

	public async Task<IReadOnlyList<string>> GetApiVersionsAsync(CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint.MetadataUri());
		using var response = await SendAsync(request,
				YtmDesktopHttpClients.Fast,
				authenticated: false,
				cancellationToken)
			.ConfigureAwait(false);

		using var document = await ParseAsync(response, cancellationToken)
			.ConfigureAwait(false);

		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object ||
			!root.TryGetProperty("apiVersions", out var versions) ||
			versions.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<string>(versions.GetArrayLength());
		foreach (var version in versions.EnumerateArray())
		{
			if (version.ValueKind == JsonValueKind.String && version.GetString() is { Length: > 0 } value)
			{
				result.Add(value);
			}
		}

		return result;
	}

	public async Task<string> RequestAuthCodeAsync(
		string appId,
		string appName,
		string appVersion,
		CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["appId"] = appId,
			["appName"] = appName,
			["appVersion"] = appVersion
		};

		using var content = JsonContent.Create(body, options: _jsonOptions);
		using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.ApiUri("auth/requestcode"))
		{
			Content = content
		};

		using var response = await SendAsync(request,
				YtmDesktopHttpClients.Fast,
				authenticated: false,
				cancellationToken)
			.ConfigureAwait(false);

		using var document = await ParseAsync(response, cancellationToken).ConfigureAwait(false);
		return ReadRequiredString(document.RootElement, "code", response.StatusCode);
	}

	public async Task<string> RequestTokenAsync(string appId, string code, CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["appId"] = appId,
			["code"] = code
		};

		using var content = JsonContent.Create(body, options: _jsonOptions);
		using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.ApiUri("auth/request"))
		{
			Content = content
		};

		using var response = await SendAsync(request,
				YtmDesktopHttpClients.Slow,
				authenticated: false,
				cancellationToken)
			.ConfigureAwait(false);

		using var document = await ParseAsync(response, cancellationToken).ConfigureAwait(false);
		return ReadRequiredString(document.RootElement, "token", response.StatusCode);
	}

	public async Task<JsonElement> GetStateAsync(CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint.ApiUri("state"));
		using var response = await SendAsync(request,
				YtmDesktopHttpClients.Fast,
				authenticated: true,
				cancellationToken)
			.ConfigureAwait(false);

		using var document = await ParseAsync(response, cancellationToken).ConfigureAwait(false);

		return document.RootElement.Clone();
	}

	public async Task<IReadOnlyList<YtmPlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint.ApiUri("playlists"));
		using var response = await SendAsync(request,
				YtmDesktopHttpClients.Slow,
				authenticated: true,
				cancellationToken)
			.ConfigureAwait(false);

		using var document = await ParseAsync(response, cancellationToken).ConfigureAwait(false);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<YtmPlaylist>(root.GetArrayLength());
		foreach (var entry in root.EnumerateArray())
		{
			if (entry.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (ReadString(entry, "id") is { } id && ReadString(entry, "title") is { } title)
			{
				result.Add(new YtmPlaylist(id, title));
			}
		}

		return result;
	}

	public async Task SendCommandAsync(string command, object? data, CancellationToken cancellationToken)
	{
		using var content = JsonContent.Create(new CommandBody(command, data), options: _jsonOptions);
		using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.ApiUri("command")) { Content = content };

		using var response = await SendAsync(request,
				YtmDesktopHttpClients.Fast,
				authenticated: true,
				cancellationToken)
			.ConfigureAwait(false);

		response.Dispose();
	}

	public void Dispose()
	{
		_disposed = true;
		_token = null;
	}

	private async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		HttpClient client,
		bool authenticated,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (authenticated && _token is { Length: > 0 } token)
		{
			// The Companion Server wants the raw token with no scheme; HttpRequestHeaders.Authorization
			// cannot express that, so it is added without validation instead of through the typed setter.
			request.Headers.TryAddWithoutValidation("Authorization", token);
		}

		var response = _send is not null
			? await _send(request, cancellationToken).ConfigureAwait(false)
			: await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

		await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
		return response;
	}

	private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		string body;
		try
		{
			body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
			body = string.Empty;
		}

		var errorCode = ReadErrorCode(body);
		var retryAfter = ReadRetryAfter(response);
		var message = errorCode is null
			? string.Create(CultureInfo.InvariantCulture,
				$"The Companion Server answered {(int)response.StatusCode} {response.ReasonPhrase}.")
			: string.Create(CultureInfo.InvariantCulture,
				$"The Companion Server answered {(int)response.StatusCode} ({errorCode}).");

		if (response.StatusCode == HttpStatusCode.Unauthorized ||
			string.Equals(errorCode, YtmErrorCodes.Unauthenticated, StringComparison.Ordinal))
		{
			throw new YtmDesktopAuthorizationException(message);
		}

		throw new YtmDesktopApiException(message, response.StatusCode, errorCode, retryAfter);
	}

	private static async Task<JsonDocument> ParseAsync(
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		string body;
		try
		{
			body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException ex)
		{
			throw new YtmDesktopApiException("The Companion Server's response could not be read.",
				ex,
				response.StatusCode);
		}

		try
		{
			return JsonDocument.Parse(body.Length == 0 ? "{}" : body);
		}
		catch (JsonException ex)
		{
			throw new YtmDesktopApiException("The Companion Server returned a response that could not be read.",
				ex,
				response.StatusCode);
		}
	}

	private static string? ReadErrorCode(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(body);
			return ReadString(document.RootElement, "code");
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
	{
		var header = response.Headers.RetryAfter;
		if (header is null)
		{
			return null;
		}

		if (header.Delta is { } delta)
		{
			return delta;
		}

		if (header.Date is { } date)
		{
			var remaining = date - DateTimeOffset.UtcNow;
			return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
		}

		return null;
	}

	private static string ReadRequiredString(JsonElement root, string property, HttpStatusCode statusCode)
		=> ReadString(root, property) ??
			throw new YtmDesktopApiException($"The Companion Server's response did not include '{property}'.",
				statusCode);

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private sealed record CommandBody(string Command, object? Data);
}
