using System.Globalization;
using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed class ConnectIdentityClient : IConnectIdentityClient, IDisposable
{
	public const string HttpClientName = "macrodeck-connect";

	private readonly HttpClient _http;
	private readonly bool _ownsClient;

	public ConnectIdentityClient(IHttpClientFactory httpClientFactory)
	{
		_http = httpClientFactory.CreateClient(HttpClientName);
		_ownsClient = false;
	}

	internal ConnectIdentityClient(HttpMessageHandler handler)
	{
		_http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };
		_ownsClient = true;
	}

	public async Task<ConnectDeviceAuthorization> RequestDeviceAuthorization(CancellationToken cancellationToken)
	{
		using var response = await Post(ConnectEndpoints.DeviceAuthorizationEndpoint,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["client_id"] = ConnectEndpoints.ClientId, ["scope"] = ConnectEndpoints.Scope
			},
			cancellationToken);

		var body = await ReadBody(response, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw Classify(response, body);
		}

		using var document = ParseOrThrow(body);
		var root = document.RootElement;

		var deviceCode = ReadString(root, "device_code");
		var userCode = ReadString(root, "user_code");
		var verificationUri = ReadUri(root, "verification_uri");

		if (deviceCode is null || userCode is null || verificationUri is null)
		{
			throw new ConnectAuthTransientException("Macro Deck Connect returned an incomplete device authorization.");
		}

		return new ConnectDeviceAuthorization(deviceCode,
			userCode,
			verificationUri,
			ReadUri(root, "verification_uri_complete") ?? verificationUri,
			TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 900),
			// The issuer sends interval as a JSON string rather than a number, which ReadInt tolerates and
			// a direct GetInt32 would not.
			TimeSpan.FromSeconds(ReadInt(root, "interval") ?? 5));
	}

	public async Task<ConnectDevicePollResult> PollDeviceToken(
		string deviceCode,
		CancellationToken cancellationToken)
	{
		using var response = await Post(ConnectEndpoints.TokenEndpoint,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["grant_type"] = ConnectEndpoints.DeviceCodeGrantType,
				["device_code"] = deviceCode,
				["client_id"] = ConnectEndpoints.ClientId
			},
			cancellationToken);

		var body = await ReadBody(response, cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			return new ConnectDevicePollResult(ConnectDevicePollStatus.Success, ReadTokens(body));
		}

		// Deliberately not Classify: there, access_denied means the account is suspended. On a device poll
		// it means the user declined this authorization, which must not touch the session's suspension
		// state or its stored credential.
		var (error, description) = ReadError(body);

		return error switch
		{
			"authorization_pending" => new ConnectDevicePollResult(ConnectDevicePollStatus.Pending),
			"slow_down" => new ConnectDevicePollResult(ConnectDevicePollStatus.SlowDown),
			"access_denied" => new ConnectDevicePollResult(ConnectDevicePollStatus.Denied, null, description),
			// invalid_grant is what an unknown or already redeemed device code answers, which for a poll is
			// indistinguishable from an expired one.
			"expired_token" or "invalid_grant" => new ConnectDevicePollResult(ConnectDevicePollStatus.Expired,
				null,
				description),
			_ when response.StatusCode is HttpStatusCode.TooManyRequests =>
				throw new ConnectAuthTransientException("Macro Deck Connect is rate-limiting the token endpoint.")
				{
					IsRateLimit = true, RetryAfter = ReadRetryAfter(response)
				},
			_ => throw new ConnectAuthTransientException(error is { Length: > 0 }
				? $"Macro Deck Connect answered {(int)response.StatusCode} ({error}) while polling."
				: $"Macro Deck Connect answered {(int)response.StatusCode} while polling.")
		};
	}

	public Task<ConnectTokenResponse> Refresh(string refreshToken, CancellationToken cancellationToken)
		=> RequestToken(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["grant_type"] = "refresh_token",
				["refresh_token"] = refreshToken,
				["client_id"] = ConnectEndpoints.ClientId
			},
			cancellationToken);

	public async Task Revoke(string refreshToken, CancellationToken cancellationToken)
	{
		using var response = await Post(ConnectEndpoints.RevokeEndpoint,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["token"] = refreshToken,
				["token_type_hint"] = "refresh_token",
				["client_id"] = ConnectEndpoints.ClientId
			},
			cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var body = await ReadBody(response, cancellationToken);
			throw Classify(response, body);
		}
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private async Task<ConnectTokenResponse> RequestToken(
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		using var response = await Post(ConnectEndpoints.TokenEndpoint, form, cancellationToken);
		var body = await ReadBody(response, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw Classify(response, body);
		}

		return ReadTokens(body);
	}

	private static ConnectTokenResponse ReadTokens(string body)
	{
		using var document = ParseOrThrow(body);
		var root = document.RootElement;

		var accessToken = ReadString(root, "access_token");
		var refreshToken = ReadString(root, "refresh_token");
		var idToken = ReadString(root, "id_token");

		if (accessToken is null || refreshToken is null || idToken is null)
		{
			throw new ConnectAuthTransientException("Macro Deck Connect returned an incomplete token response.");
		}

		return new ConnectTokenResponse(accessToken,
			refreshToken,
			idToken,
			TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 600));
	}

	private static JsonDocument ParseOrThrow(string body)
	{
		try
		{
			return JsonDocument.Parse(body);
		}
		catch (JsonException ex)
		{
			throw new ConnectAuthTransientException("Macro Deck Connect returned an unreadable response.", ex);
		}
	}

	private async Task<HttpResponseMessage> Post(
		string url,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		// A public client: the client_id travels in the form body and there is no secret to send anywhere,
		// in any header or any field.
		using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };

		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ConnectAuthTransientException("The request to Macro Deck Connect timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new ConnectAuthTransientException("Macro Deck Connect could not be reached.", ex);
		}
	}

	private static async Task<string> ReadBody(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			return await response.Content.ReadAsStringAsync(cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			throw new ConnectAuthTransientException("The response from Macro Deck Connect could not be read.", ex);
		}
	}

	private static Exception Classify(HttpResponseMessage response, string body)
	{
		var (error, description) = ReadError(body);

		// access_denied for a suspended account does not revoke the authorization, so it must never be
		// treated as a reason to drop the credential.
		if (string.Equals(error, "access_denied", StringComparison.Ordinal))
		{
			return new ConnectAccountSuspendedException(description ?? "This account has been suspended.");
		}

		if (string.Equals(error, "invalid_grant", StringComparison.Ordinal))
		{
			return new ConnectAuthRejectedException(description is { Length: > 0 }
				? $"Macro Deck Connect rejected the credential: {description}"
				: "Macro Deck Connect rejected the credential.");
		}

		if (response.StatusCode is HttpStatusCode.TooManyRequests)
		{
			return new ConnectAuthTransientException("Macro Deck Connect is rate-limiting the token endpoint.")
			{
				IsRateLimit = true, RetryAfter = ReadRetryAfter(response)
			};
		}

		// invalid_client, invalid_request and unsupported_grant_type are our own bug, not the user's: the
		// credential is kept and retried on the normal schedule so that shipping a fix restores the
		// session without an interactive sign-in.
		return new ConnectAuthTransientException(error is { Length: > 0 }
			? $"Macro Deck Connect answered {(int)response.StatusCode} ({error})."
			: $"Macro Deck Connect answered {(int)response.StatusCode}.");
	}

	private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
	{
		var retryAfter = response.Headers.RetryAfter;

		if (retryAfter?.Delta is { } delta)
		{
			return delta;
		}

		return retryAfter?.Date is { } date && date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : null;
	}

	private static (string? Error, string? Description) ReadError(string body)
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

	private static Uri? ReadUri(JsonElement root, string name)
		=> ReadString(root, name) is { Length: > 0 } value &&
			Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
			uri.Scheme == Uri.UriSchemeHttps
				? uri
				: null;

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
}
