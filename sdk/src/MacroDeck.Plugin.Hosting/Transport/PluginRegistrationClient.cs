using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Credentials;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>The REST half of the handshake: discovery, registration and session creation.</summary>
internal sealed class PluginRegistrationClient(IHttpClientFactory httpClientFactory)
{
	/// <summary>Name of the configured <see cref="HttpClient" /> used for every protocol call.</summary>
	public const string HttpClientName = "MacroDeck.Plugin.Protocol";

	/// <summary>What the host supports, fetched before any credential is needed.</summary>
	public async Task<PluginProtocolDescriptor> GetProtocolAsync(CancellationToken cancellationToken)
	{
		using var client = Create();
		using var response = await client.GetAsync(ProtocolConstants.ProtocolDiscoveryPath, cancellationToken);

		await EnsureSuccessAsync(response, "read the protocol descriptor", cancellationToken);

		return await ReadAsync<PluginProtocolDescriptor>(response, cancellationToken);
	}

	/// <summary>
	/// Registers the plugin, exchanging a one-time enrollment token for a secret the host will never
	/// show again.
	/// </summary>
	public async Task<PluginRegistrationResponse> RegisterAsync(
		string pluginId,
		string displayName,
		string enrollmentToken,
		CancellationToken cancellationToken)
	{
		using var client = Create();
		using var request = new HttpRequestMessage(HttpMethod.Post, ProtocolConstants.RegistrationPath)
		{
			Content = JsonContent.Create(new PluginRegistrationRequest
					{ PluginId = pluginId, DisplayName = displayName },
				options: PluginProtocolJson.Options)
		};

		request.Headers.Add(PluginAuthDefaults.EnrollmentTokenHeaderName, enrollmentToken);

		using var response = await client.SendAsync(request, cancellationToken);

		await ThrowIfDeveloperModeDisabledAsync(response, "enrol", cancellationToken);

		if (response.StatusCode == HttpStatusCode.Conflict)
		{
			throw new PluginRegistrationException(
				$"The host already has a plugin registered as '{pluginId}'. Remove it there, or start " +
				"this plugin with the secret it was issued.",
				response.StatusCode);
		}

		await EnsureSuccessAsync(response, "register the plugin", cancellationToken);

		return await ReadAsync<PluginRegistrationResponse>(response, cancellationToken);
	}

	/// <summary>
	/// Opens a session. This is where the version and the capability catalogue are negotiated, once
	/// and authoritatively; the later <c>session.hello</c> only asserts the outcome.
	/// </summary>
	public async Task<PluginSessionResponse> CreateSessionAsync(
		PluginCredentials credentials,
		PluginSessionRequest sessionRequest,
		CancellationToken cancellationToken)
	{
		using var client = Create();
		using var request = new HttpRequestMessage(HttpMethod.Post, ProtocolConstants.SessionsPath)
		{
			Content = JsonContent.Create(sessionRequest, options: PluginProtocolJson.Options)
		};

		request.Headers.Add(PluginAuthDefaults.PluginIdHeaderName, credentials.PluginId);
		request.Headers.Add(PluginAuthDefaults.PluginSecretHeaderName, credentials.Secret);

		using var response = await client.SendAsync(request, cancellationToken);

		await ThrowIfDeveloperModeDisabledAsync(response, "connect", cancellationToken);

		await EnsureSuccessAsync(response, "open a session", cancellationToken);

		return await ReadAsync<PluginSessionResponse>(response, cancellationToken);
	}

	/// <summary>
	/// Ends a session explicitly, so the host releases the slot instead of waiting for it to expire.
	/// Best effort by design: a failure here is reported by the caller and never blocks shutdown.
	/// </summary>
	public async Task DeleteSessionAsync(string sessionId, string sessionToken, CancellationToken cancellationToken)
	{
		using var client = Create();
		using var request = new HttpRequestMessage(HttpMethod.Delete,
			$"{ProtocolConstants.SessionsPath}/{Uri.EscapeDataString(sessionId)}");

		request.Headers.Add(PluginAuthDefaults.AuthorizationHeaderName,
			$"{PluginAuthDefaults.BearerScheme} {sessionToken}");

		using var response = await client.SendAsync(request, cancellationToken);
		await EnsureSuccessAsync(response, "end the session", cancellationToken);
	}

	/// <summary>
	/// Turns the host's Developer Mode refusal into a message that names the setting. Deliberately not
	/// fatal, unlike a plain 403: the whole point is that the user can still switch Developer Mode on,
	/// and the host does not charge this refusal to any throttle, so the reconnect loop may keep asking.
	/// </summary>
	private static async Task ThrowIfDeveloperModeDisabledAsync(
		HttpResponseMessage response,
		string what,
		CancellationToken cancellationToken)
	{
		if (response.StatusCode != HttpStatusCode.Forbidden)
		{
			return;
		}

		ProtocolError? error = null;
		try
		{
			error = await response.Content.ReadFromJsonAsync<ProtocolError>(PluginProtocolJson.Options,
				cancellationToken);
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException)
		{
			// A 403 that is not the protocol's error shape at all - a proxy, say. Fall through and let
			// the generic handling report it.
		}

		if (error?.Details is null ||
			!error.Details.TryGetValue("reason", out var reason) ||
			!string.Equals(reason, ProtocolErrorReasons.DeveloperModeDisabled, StringComparison.Ordinal))
		{
			return;
		}

		throw new PluginRegistrationException(
			$"Developer Mode is disabled in Macro Deck, so this plugin cannot {what}. Enable it under " +
			"Settings > Developer - this plugin keeps waiting and continues as soon as you do.",
			response.StatusCode,
			fatal: false);
	}

	private HttpClient Create()
	{
		var client = httpClientFactory.CreateClient(HttpClientName);
		client.Timeout = ProtocolTimeouts.DefaultRequest;
		return client;
	}

	private static Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
		=> ProtocolHttpHelpers.ReadAsync<T>(response, cancellationToken);

	private static Task EnsureSuccessAsync(
		HttpResponseMessage response,
		string what,
		CancellationToken cancellationToken)
		=> ProtocolHttpHelpers.EnsureSuccessAsync(response, what, cancellationToken);
}

/// <summary>A REST call in the handshake failed.</summary>
internal sealed class PluginRegistrationException : Exception
{
	private readonly bool? _fatalOverride;

	public PluginRegistrationException(string message, HttpStatusCode statusCode)
		: base(message)
		=> StatusCode = statusCode;

	/// <summary>
	/// Some failures are fatal regardless of what <see cref="StatusCode" /> would otherwise imply - e.g.
	/// a 404 on pairing creation, which means the host predates pairing rather than anything about this
	/// particular request.
	/// </summary>
	public PluginRegistrationException(string message, HttpStatusCode statusCode, bool fatal)
		: base(message)
	{
		StatusCode = statusCode;
		_fatalOverride = fatal;
	}

	public PluginRegistrationException()
	{
	}

	public PluginRegistrationException(string message)
		: base(message)
	{
	}

	public PluginRegistrationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public HttpStatusCode StatusCode { get; }

	/// <summary>
	/// Whether retrying could ever help. Credentials the host rejects and a request it considers
	/// malformed will be rejected identically forever; everything else is worth another attempt.
	/// </summary>
	public bool IsFatal => _fatalOverride ??
		StatusCode is HttpStatusCode.Unauthorized
			or HttpStatusCode.Forbidden
			or HttpStatusCode.BadRequest
			or HttpStatusCode.UnprocessableContent;
}

/// <summary>
/// Response reading and error translation shared by <see cref="PluginRegistrationClient" /> and
/// <see cref="PluginPairingClient" />, so the two REST clients of the handshake agree on what an
/// unsuccessful or empty response means without one making the other's internals public.
/// </summary>
internal static class ProtocolHttpHelpers
{
	public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		var value = await response.Content.ReadFromJsonAsync<T>(PluginProtocolJson.Options, cancellationToken);

		if (value is null)
		{
			throw new PluginRegistrationException(
				$"The host returned an empty body where a {typeof(T).Name} was expected.",
				response.StatusCode);
		}

		return value;
	}

	public static async Task EnsureSuccessAsync(
		HttpResponseMessage response,
		string what,
		CancellationToken cancellationToken)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		// The body is included because the host's problem details name the actual reason, and without
		// it a 400 during the handshake is indistinguishable from any other 400.
		var body = await response.Content.ReadAsStringAsync(cancellationToken);

		throw new PluginRegistrationException(
			$"The host refused to {what}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}".TrimEnd(),
			response.StatusCode);
	}
}
