using System.Globalization;
using System.Net;
using System.Text.Json;
using MacroDeckHost.Integrations.Http;

namespace MacroDeckHost.Integrations.YouTube.Auth;

internal sealed class YouTubeOAuthClient : IYouTubeOAuthClient
{
	private const string RateLimitExceeded = "rate_limit_exceeded";

	private static readonly HttpClient _shared = CreateClient(IntegrationHttp.CreateHandler(), disposeHandler: true);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly TimeProvider _time;

	public YouTubeOAuthClient()
	{
		_http = _shared;
		_ownsClient = false;
		_time = TimeProvider.System;
	}

	internal YouTubeOAuthClient(HttpMessageHandler handler, TimeProvider? timeProvider = null)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
		_time = timeProvider ?? TimeProvider.System;
	}

	public async Task<YouTubeDeviceCode> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client_id"] = clientId,
			["scope"] = YouTubeOAuthEndpoints.Scope
		};

		using var response = await PostFormAsync(YouTubeOAuthEndpoints.DeviceCode, form, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var error = ReadError(body);
			if (IsTransient(response.StatusCode) || error.Code is RateLimitExceeded)
			{
				throw new YouTubeOAuthTransientException(
					$"Google answered {(int)response.StatusCode} {error.Code} to the device code request.");
			}

			throw new YouTubeOAuthRejectedException(
				$"Google refused the device code request ({(int)response.StatusCode} {error.Code}): {error.Description}",
				error.Code);
		}

		using var document = Parse(body);
		var root = document.RootElement;
		var deviceCode = ReadString(root, "device_code");
		var userCode = ReadString(root, "user_code");
		var verificationUrl = ReadString(root, "verification_url") ?? ReadString(root, "verification_uri");

		if (deviceCode is null || userCode is null || verificationUrl is null)
		{
			throw new YouTubeOAuthTransientException("Google returned an incomplete device authorization.");
		}

		return new YouTubeDeviceCode(deviceCode,
			userCode,
			verificationUrl,
			TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 1800),
			TimeSpan.FromSeconds(ReadInt(root, "interval") ?? 5));
	}

	public async Task<YouTubeTokenPoll> PollTokenAsync(
		string clientId,
		string clientSecret,
		string deviceCode,
		CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client_id"] = clientId,
			["client_secret"] = clientSecret,
			["device_code"] = deviceCode,
			["grant_type"] = YouTubeOAuthEndpoints.DeviceCodeGrantType
		};

		using var response = await PostFormAsync(YouTubeOAuthEndpoints.Token, form, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			using var document = Parse(body);
			var tokens = ReadTokens(document.RootElement, previousRefreshToken: null)
				?? throw new YouTubeOAuthTransientException("Google returned an incomplete token response.");

			return new YouTubeTokenPoll(YouTubeTokenPollStatus.Success, tokens);
		}

		if (IsTransient(response.StatusCode))
		{
			throw new YouTubeOAuthTransientException(
				$"Google answered {(int)response.StatusCode} while polling for the token.");
		}

		var code = ReadError(body).Code;
		return new YouTubeTokenPoll(ClassifyPollError(code), ErrorCode: code);
	}

	public async Task<YouTubeTokens> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client_id"] = clientId,
			["client_secret"] = clientSecret,
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken
		};

		using var response = await PostFormAsync(YouTubeOAuthEndpoints.Token, form, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			using var document = Parse(body);
			return ReadTokens(document.RootElement, refreshToken)
				?? throw new YouTubeOAuthTransientException("Google returned an incomplete token response.");
		}

		var error = ReadError(body);

		// A 5xx or a rate limit is the service having a bad minute; the stored refresh token is still
		// the right one and must not be thrown away over it.
		if (IsTransient(response.StatusCode) || error.Code is RateLimitExceeded)
		{
			throw new YouTubeOAuthTransientException(
				$"Google answered {(int)response.StatusCode} while refreshing the token.");
		}

		throw new YouTubeOAuthRejectedException(
			$"Google rejected the refresh token ({(int)response.StatusCode} {error.Code}): {error.Description}",
			error.Code);
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private static YouTubeTokenPollStatus ClassifyPollError(string? code)
		=> code switch
		{
			"authorization_pending" => YouTubeTokenPollStatus.Pending,
			"slow_down" or RateLimitExceeded => YouTubeTokenPollStatus.SlowDown,
			"access_denied" => YouTubeTokenPollStatus.Denied,
			"expired_token" or "invalid_grant" => YouTubeTokenPollStatus.Expired,
			_ => YouTubeTokenPollStatus.Rejected
		};

	private static bool IsTransient(HttpStatusCode status)
		=> status is HttpStatusCode.TooManyRequests || (int)status >= 500;

	private async Task<HttpResponseMessage> PostFormAsync(
		string url,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		// The content is owned by the request and disposed with it, so it must not be disposed here.
		using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };

		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new YouTubeOAuthTransientException("The request to Google timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new YouTubeOAuthTransientException("Google could not be reached.", ex);
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
			throw new YouTubeOAuthTransientException("The response from Google could not be read.", ex);
		}
	}

	private YouTubeTokens? ReadTokens(JsonElement root, string? previousRefreshToken)
	{
		var accessToken = ReadString(root, "access_token");
		var refreshToken = ReadString(root, "refresh_token") ?? previousRefreshToken;

		if (accessToken is null || refreshToken is null)
		{
			return null;
		}

		var expiresIn = ReadInt(root, "expires_in") ?? 3600;
		var scopes = ReadString(root, "scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

		return new YouTubeTokens(accessToken, refreshToken, _time.GetUtcNow().AddSeconds(expiresIn), scopes);
	}

	private static (string? Code, string? Description) ReadError(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return (null, null);
		}

		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			return root.ValueKind is JsonValueKind.Object
				? (ReadString(root, "error"), ReadString(root, "error_description"))
				: (null, null);
		}
		catch (JsonException)
		{
			return (null, null);
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
			throw new YouTubeOAuthTransientException("Google returned a response that could not be read.", ex);
		}
	}

	private static string? ReadString(JsonElement root, string name)
		=> root.ValueKind is JsonValueKind.Object &&
			root.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.String
				? value.GetString()
				: null;

	private static int? ReadInt(JsonElement root, string name)
	{
		if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty(name, out var value))
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

	private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
		=> new(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(15) };
}
