using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

internal interface IMicrosoftOAuthClient
{
	Task<MicrosoftTokenResponse> ExchangeCodeAsync(
		string clientId,
		string tenant,
		string code,
		string codeVerifier,
		string redirectUri,
		CancellationToken cancellationToken);

	Task<MicrosoftTokenResponse> RefreshAsync(
		string clientId,
		string tenant,
		string refreshToken,
		CancellationToken cancellationToken);
}

internal sealed record MicrosoftIdentity(string? AccountKey, string? Email, string? Name);

internal sealed record MicrosoftTokenResponse(
	string AccessToken,
	string? RefreshToken,
	TimeSpan ExpiresIn,
	string? Scope,
	string? IdToken)
{
	public MicrosoftIdentity Identity()
	{
		var parts = IdToken?.Split('.');
		if (parts is not { Length: 3 })
		{
			return new MicrosoftIdentity(null, null, null);
		}

		try
		{
			// Not verified: the token came straight from Microsoft's token endpoint over TLS, which is
			// the case OpenID Connect allows to skip signature validation for.
			using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
			var root = payload.RootElement;
			var oid = ReadString(root, "oid");
			var tid = ReadString(root, "tid");
			var email = ReadString(root, "email") ?? ReadString(root, "preferred_username");
			return new MicrosoftIdentity(oid is null ? null : $"{tid}/{oid}", email, ReadString(root, "name"));
		}
		catch (Exception ex) when (ex is FormatException or JsonException)
		{
			return new MicrosoftIdentity(null, null, null);
		}
	}

	private static string? ReadString(JsonElement root, string name)
		=> root.ValueKind is JsonValueKind.Object &&
			root.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.String &&
			value.GetString() is { Length: > 0 } text
				? text
				: null;
}

internal sealed class MicrosoftOAuthTransientException : Exception
{
	public MicrosoftOAuthTransientException()
	{
	}

	public MicrosoftOAuthTransientException(string message)
		: base(message)
	{
	}

	public MicrosoftOAuthTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class MicrosoftOAuthRejectedException : Exception
{
	public MicrosoftOAuthRejectedException()
	{
	}

	public MicrosoftOAuthRejectedException(string message)
		: base(message)
	{
	}

	public MicrosoftOAuthRejectedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public MicrosoftOAuthRejectedException(string message, string? error, IReadOnlyList<int> errorCodes)
		: base(message)
	{
		Error = error;
		ErrorCodes = errorCodes;
	}

	public string? Error { get; }

	public IReadOnlyList<int> ErrorCodes { get; } = [];
}

internal sealed class MicrosoftOAuthClient : IMicrosoftOAuthClient, IDisposable
{
	private static readonly HttpClient _shared = CreateClient(new SocketsHttpHandler(), disposeHandler: true);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;

	public MicrosoftOAuthClient()
	{
		_http = _shared;
		_ownsClient = false;
	}

	internal MicrosoftOAuthClient(HttpMessageHandler handler)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
	}

	public Task<MicrosoftTokenResponse> ExchangeCodeAsync(
		string clientId,
		string tenant,
		string code,
		string codeVerifier,
		string redirectUri,
		CancellationToken cancellationToken)
		=> RequestTokenAsync(tenant,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["client_id"] = clientId,
				["scope"] = MicrosoftOAuth.RequestedScopes,
				["code"] = code,
				["code_verifier"] = codeVerifier,
				["grant_type"] = "authorization_code",
				["redirect_uri"] = redirectUri
			},
			cancellationToken);

	public Task<MicrosoftTokenResponse> RefreshAsync(
		string clientId,
		string tenant,
		string refreshToken,
		CancellationToken cancellationToken)
		=> RequestTokenAsync(tenant,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["client_id"] = clientId,
				["scope"] = MicrosoftOAuth.RequestedScopes,
				["refresh_token"] = refreshToken,
				["grant_type"] = "refresh_token"
			},
			cancellationToken);

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private async Task<MicrosoftTokenResponse> RequestTokenAsync(
		string tenant,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, MicrosoftOAuth.TokenEndpoint(tenant))
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
				throw new MicrosoftOAuthTransientException(
					"Microsoft returned a token response without an access token.");

			return new MicrosoftTokenResponse(accessToken,
				ReadString(root, "refresh_token"),
				TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 3600),
				ReadString(root, "scope"),
				ReadString(root, "id_token"));
		}

		var (error, codes) = ReadError(body);

		// A 5xx, a rate limit or an outage reported as an error code is Microsoft having a bad minute;
		// the stored refresh token is still the right one and must not be given up over it.
		if (response.StatusCode is HttpStatusCode.TooManyRequests ||
			(int)response.StatusCode >= 500 ||
			error is "temporarily_unavailable")
		{
			throw new MicrosoftOAuthTransientException(
				$"Microsoft answered {(int)response.StatusCode} at the token endpoint.");
		}

		throw new MicrosoftOAuthRejectedException(error is { Length: > 0 }
				? $"Microsoft refused the token request: {error} {string.Join(',', codes)}".TrimEnd()
				: $"Microsoft refused the token request ({(int)response.StatusCode}).",
			error,
			codes);
	}

	private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new MicrosoftOAuthTransientException("The request to Microsoft timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new MicrosoftOAuthTransientException("Microsoft could not be reached.", ex);
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
			throw new MicrosoftOAuthTransientException("The response from Microsoft could not be read.", ex);
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
			throw new MicrosoftOAuthTransientException("Microsoft returned a response that could not be read.", ex);
		}
	}

	private static (string? Error, IReadOnlyList<int> Codes) ReadError(string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			if (root.ValueKind is not JsonValueKind.Object)
			{
				return (null, []);
			}

			var codes = root.TryGetProperty("error_codes", out var list) && list.ValueKind is JsonValueKind.Array
				? list.EnumerateArray()
					.Where(code => code.ValueKind is JsonValueKind.Number && code.TryGetInt32(out _))
					.Select(code => code.GetInt32())
					.ToList()
				: [];
			return (ReadString(root, "error"), codes);
		}
		catch (JsonException)
		{
			return (null, []);
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
