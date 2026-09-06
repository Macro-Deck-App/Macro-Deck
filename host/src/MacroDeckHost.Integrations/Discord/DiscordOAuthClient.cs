using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Discord;

internal interface IDiscordOAuthClient
{
	Task<DiscordTokens> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		CancellationToken cancellationToken);

	Task<DiscordTokens> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken);
}

internal sealed class DiscordOAuthClient : IDiscordOAuthClient
{
	internal const string RedirectUri = "http://localhost/";

	private const string TokenEndpoint = "https://discord.com/api/oauth2/token";

	private static readonly ILogger _logger = IntegrationLog.For<DiscordOAuthClient>(DiscordIntegration.IntegrationId);
	private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

	public Task<DiscordTokens> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		CancellationToken cancellationToken)
		=> PostAsync(clientId,
			clientSecret,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["grant_type"] = "authorization_code",
				["code"] = code,
				["redirect_uri"] = RedirectUri
			},
			cancellationToken);

	public Task<DiscordTokens> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
		=> PostAsync(clientId,
			clientSecret,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["grant_type"] = "refresh_token",
				["refresh_token"] = refreshToken
			},
			cancellationToken);

	private static async Task<DiscordTokens> PostAsync(
		string clientId,
		string clientSecret,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		form["client_id"] = clientId;
		form["client_secret"] = clientSecret;

		using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
		{
			Content = new FormUrlEncodedContent(form)
		};

		using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			_logger.Warning("Discord token request failed with {Status}: {Body}", (int)response.StatusCode, body);
			throw new DiscordOAuthException(DescribeFailure(body));
		}

		var payload = await response.Content
			.ReadFromJsonAsync<TokenResponse>(cancellationToken)
			.ConfigureAwait(false);

		if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
		{
			throw new DiscordOAuthException("Discord returned an empty token response.");
		}

		var expiresAt = payload.ExpiresIn > 0
			? DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn)
			: (DateTimeOffset?)null;

		return new DiscordTokens(payload.AccessToken, payload.RefreshToken, expiresAt, payload.Scope);
	}

	private static string DescribeFailure(string body)
	{
		var error = ReadError(body);
		return error switch
		{
			"invalid_client" => "Discord rejected the Client ID or Client Secret.",
			"invalid_grant" => "The authorization expired before it could be redeemed. Start the setup again.",
			"invalid_scope" => "Your Discord application is not allowed to request the RPC scopes.",
			null => "Discord rejected the token request.",
			_ => string.Create(CultureInfo.InvariantCulture, $"Discord rejected the token request ({error}).")
		};
	}

	private static string? ReadError(string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			return document.RootElement.ValueKind == JsonValueKind.Object &&
				document.RootElement.TryGetProperty("error", out var error) &&
				error.ValueKind == JsonValueKind.String
					? error.GetString()
					: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private sealed record TokenResponse(
		[property: JsonPropertyName("access_token")]
		string? AccessToken,
		[property: JsonPropertyName("refresh_token")]
		string? RefreshToken,
		[property: JsonPropertyName("expires_in")]
		int ExpiresIn,
		[property: JsonPropertyName("scope")] string? Scope);
}

internal sealed class DiscordOAuthException : Exception
{
	public DiscordOAuthException(string message)
		: base(message)
	{
	}

	public DiscordOAuthException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public DiscordOAuthException()
	{
	}
}
