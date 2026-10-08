using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MacroDeckHost.Integrations.Http;

namespace MacroDeckHost.Integrations.GoogleCalendar;

internal interface IGoogleOAuthClient
{
	Task<GoogleTokenResponse> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		string codeVerifier,
		string redirectUri,
		CancellationToken cancellationToken);

	Task<GoogleTokenResponse> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken);

	Task<string?> GetEmailAsync(string accessToken, CancellationToken cancellationToken);
}

internal sealed record GoogleTokenResponse(
	string AccessToken,
	string? RefreshToken,
	TimeSpan ExpiresIn,
	string? Scope,
	string? IdToken)
{
	public string? EmailFromIdToken()
	{
		var parts = IdToken?.Split('.');
		if (parts is not { Length: 3 })
		{
			return null;
		}

		try
		{
			// Not verified: the token came straight from Google's token endpoint over TLS, which is
			// the case OpenID Connect allows to skip signature validation for.
			using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
			return payload.RootElement.TryGetProperty("email", out var email) &&
				email.ValueKind is JsonValueKind.String
					? email.GetString()
					: null;
		}
		catch (Exception ex) when (ex is FormatException or JsonException)
		{
			return null;
		}
	}
}

internal sealed class GoogleOAuthTransientException : Exception
{
	public GoogleOAuthTransientException()
	{
	}

	public GoogleOAuthTransientException(string message)
		: base(message)
	{
	}

	public GoogleOAuthTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class GoogleOAuthRejectedException : Exception
{
	public GoogleOAuthRejectedException()
	{
	}

	public GoogleOAuthRejectedException(string message)
		: base(message)
	{
	}

	public GoogleOAuthRejectedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class GoogleOAuthClient : IGoogleOAuthClient, IDisposable
{
	private static readonly HttpClient _shared = CreateClient(IntegrationHttp.CreateHandler(), disposeHandler: true);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;

	public GoogleOAuthClient()
	{
		_http = _shared;
		_ownsClient = false;
	}

	internal GoogleOAuthClient(HttpMessageHandler handler)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
	}

	public Task<GoogleTokenResponse> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		string codeVerifier,
		string redirectUri,
		CancellationToken cancellationToken)
		=> RequestTokenAsync(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["client_id"] = clientId,
				["client_secret"] = clientSecret,
				["code"] = code,
				["code_verifier"] = codeVerifier,
				["grant_type"] = "authorization_code",
				["redirect_uri"] = redirectUri
			},
			cancellationToken);

	public Task<GoogleTokenResponse> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
		=> RequestTokenAsync(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["client_id"] = clientId,
				["client_secret"] = clientSecret,
				["refresh_token"] = refreshToken,
				["grant_type"] = "refresh_token"
			},
			cancellationToken);

	public async Task<string?> GetEmailAsync(string accessToken, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, GoogleOAuth.UserInfoEndpoint);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using var response = await SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		using var document = Parse(await ReadBodyAsync(response, cancellationToken));
		return ReadString(document.RootElement, "email");
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private async Task<GoogleTokenResponse> RequestTokenAsync(
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, GoogleOAuth.TokenEndpoint)
		{
			Content = new FormUrlEncodedContent(form)
		};
		using var response = await SendAsync(request, cancellationToken);
		var body = await ReadBodyAsync(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			using var document = Parse(body);
			var root = document.RootElement;
			var accessToken = ReadString(root, "access_token") ??
				throw new GoogleOAuthTransientException("Google returned a token response without an access token.");

			return new GoogleTokenResponse(accessToken,
				ReadString(root, "refresh_token"),
				TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 3600),
				ReadString(root, "scope"),
				ReadString(root, "id_token"));
		}

		// A 5xx or a rate limit is Google having a bad minute; the stored refresh token is still the
		// right one and must not be given up over it.
		if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
		{
			throw new GoogleOAuthTransientException(
				$"Google answered {(int)response.StatusCode} at the token endpoint.");
		}

		throw new GoogleOAuthRejectedException(ReadError(body) is { Length: > 0 } error
			? $"Google refused the token request: {error}"
			: $"Google refused the token request ({(int)response.StatusCode}).");
	}

	private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new GoogleOAuthTransientException("The request to Google timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new GoogleOAuthTransientException("Google could not be reached.", ex);
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
			throw new GoogleOAuthTransientException("The response from Google could not be read.", ex);
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
			throw new GoogleOAuthTransientException("Google returned a response that could not be read.", ex);
		}
	}

	private static string? ReadError(string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			return document.RootElement.ValueKind is JsonValueKind.Object
				? ReadString(document.RootElement, "error")
				: null;
		}
		catch (JsonException)
		{
			return null;
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

	private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
		=> new(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(15) };
}
