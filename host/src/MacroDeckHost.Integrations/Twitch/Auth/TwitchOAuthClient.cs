using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Twitch.Auth;

internal sealed class TwitchOAuthClient : ITwitchOAuthClient, IDisposable
{
	private static readonly HttpClient _shared = CreateClient(new SocketsHttpHandler(), disposeHandler: true);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;

	public TwitchOAuthClient()
	{
		_http = _shared;
		_ownsClient = false;
	}

	internal TwitchOAuthClient(HttpMessageHandler handler)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
	}

	public async Task<TwitchDeviceCode> RequestDeviceCodeAsync(
		string clientId,
		string scopes,
		CancellationToken cancellationToken)
	{
		using var document = await PostFormForJsonAsync(TwitchOAuthEndpoints.Device,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["client_id"] = clientId,
				["scopes"] = scopes
			},
			cancellationToken);

		var root = document.RootElement;
		var deviceCode = ReadString(root, "device_code");
		var userCode = ReadString(root, "user_code");
		var verificationUri = ReadString(root, "verification_uri");

		if (deviceCode is null || userCode is null || verificationUri is null)
		{
			throw new TwitchOAuthTransientException("Twitch returned an incomplete device authorization.");
		}

		return new TwitchDeviceCode(deviceCode,
			userCode,
			verificationUri,
			TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 1800),
			TimeSpan.FromSeconds(ReadInt(root, "interval") ?? 5));
	}

	public async Task<TwitchTokenPollResult> PollTokenAsync(
		string clientId,
		string scopes,
		string deviceCode,
		CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client_id"] = clientId,
			["scopes"] = scopes,
			["device_code"] = deviceCode,
			["grant_type"] = TwitchOAuthEndpoints.DeviceCodeGrantType
		};

		using var response = await PostFormAsync(TwitchOAuthEndpoints.Token, form, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			using var document = Parse(body);
			return new TwitchTokenPollResult(TwitchTokenPollStatus.Success, ReadTokens(document.RootElement));
		}

		if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
		{
			return new TwitchTokenPollResult(TwitchTokenPollStatus.SlowDown);
		}

		return new TwitchTokenPollResult(ClassifyPollError(ReadError(body)));
	}

	public async Task<TwitchTokens> RefreshAsync(
		string clientId,
		string refreshToken,
		CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client_id"] = clientId,
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken
		};

		using var response = await PostFormAsync(TwitchOAuthEndpoints.Token, form, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			using var document = Parse(body);
			var tokens = ReadTokens(document.RootElement);
			return tokens ?? throw new TwitchOAuthTransientException("Twitch returned an incomplete token response.");
		}

		// A 5xx or a rate limit is the service having a bad minute; the stored refresh token is still
		// the right one and must not be thrown away over it.
		if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
		{
			throw new TwitchOAuthTransientException(
				$"Twitch answered {(int)response.StatusCode} while refreshing the token.");
		}

		throw new TwitchOAuthRejectedException(ReadError(body) is { Length: > 0 } message
			? $"Twitch rejected the refresh token: {message}"
			: "Twitch rejected the refresh token.");
	}

	public async Task<TwitchTokenIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, TwitchOAuthEndpoints.Validate);

		request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

		using var response = await SendAsync(request, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			throw new TwitchOAuthRejectedException("Twitch no longer accepts this access token.");
		}

		if (!response.IsSuccessStatusCode)
		{
			throw new TwitchOAuthTransientException(
				$"Twitch answered {(int)response.StatusCode} while validating the token.");
		}

		using var document = Parse(body);
		var root = document.RootElement;
		var userId = ReadString(root, "user_id");
		var login = ReadString(root, "login");

		if (userId is null || login is null)
		{
			throw new TwitchOAuthTransientException("Twitch returned an incomplete token validation.");
		}

		return new TwitchTokenIdentity(userId,
			login,
			ReadString(root, "client_id") ?? string.Empty,
			ReadStringArray(root, "scopes"),
			TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 0));
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private async Task<JsonDocument> PostFormForJsonAsync(
		string url,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		using var response = await PostFormAsync(url, form, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			return Parse(body);
		}

		var message = ReadError(body);
		throw (int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.TooManyRequests
			? new TwitchOAuthTransientException($"Twitch answered {(int)response.StatusCode}.")
			: new TwitchOAuthRejectedException(message is { Length: > 0 }
				? $"Twitch refused the request: {message}"
				: $"Twitch refused the request ({(int)response.StatusCode}).");
	}

	private async Task<HttpResponseMessage> PostFormAsync(
		string url,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		// The content is owned by the request and disposed with it, so it must not be disposed here.
		using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
		return await SendAsync(request, cancellationToken);
	}

	private async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TwitchOAuthTransientException("The request to Twitch timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new TwitchOAuthTransientException("Twitch could not be reached.", ex);
		}
	}

	private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			return await response.Content.ReadAsStringAsync(cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			throw new TwitchOAuthTransientException("The response from Twitch could not be read.", ex);
		}
	}

	private static TwitchTokenPollStatus ClassifyPollError(string? message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return TwitchTokenPollStatus.Pending;
		}

		var normalised = message.Replace(' ', '_').ToLowerInvariant();

		if (normalised.Contains("slow_down", StringComparison.Ordinal))
		{
			return TwitchTokenPollStatus.SlowDown;
		}

		if (normalised.Contains("denied", StringComparison.Ordinal))
		{
			return TwitchTokenPollStatus.Denied;
		}

		if (normalised.Contains("expired", StringComparison.Ordinal) ||
			normalised.Contains("invalid_device_code", StringComparison.Ordinal))
		{
			return TwitchTokenPollStatus.Expired;
		}

		return TwitchTokenPollStatus.Pending;
	}

	private static TwitchTokens? ReadTokens(JsonElement root)
	{
		var accessToken = ReadString(root, "access_token");
		var refreshToken = ReadString(root, "refresh_token");

		if (accessToken is null || refreshToken is null)
		{
			return null;
		}

		var expiresIn = ReadInt(root, "expires_in") ?? 3600;

		return new TwitchTokens(accessToken,
			refreshToken,
			DateTimeOffset.UtcNow.AddSeconds(expiresIn),
			ReadStringArray(root, "scope"));
	}

	private static string? ReadError(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			return root.ValueKind is JsonValueKind.Object
				? ReadString(root, "message") ?? ReadString(root, "error")
				: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static JsonDocument Parse(string body)
	{
		try
		{
			return JsonDocument.Parse(body);
		}
		catch (JsonException ex)
		{
			throw new TwitchOAuthTransientException("Twitch returned a response that could not be read.", ex);
		}
	}

	private static string? ReadString(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
			? value.GetString()
			: null;

	private static int? ReadInt(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.Number when value.TryGetInt32(out var number) => number,
			JsonValueKind.String when int.TryParse(value.GetString(),
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var parsed) => parsed,
			_ => null
		};
	}

	private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value))
		{
			return [];
		}

		return value.ValueKind switch
		{
			JsonValueKind.Array => value.EnumerateArray()
				.Where(item => item.ValueKind is JsonValueKind.String)
				.Select(item => item.GetString()!)
				.ToList(),
			JsonValueKind.String => value.GetString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
			_ => []
		};
	}

	private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
		=> new(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(15) };
}
