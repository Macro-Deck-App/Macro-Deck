using System.Net;
using System.Net.Http.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>The REST half of interactive pairing: create a request, poll its status, redeem it once
/// approved. Modelled on <see cref="PluginRegistrationClient" />, which stays the client for the
/// enrollment-token flow - the two are kept separate rather than merged, since pairing is unauthenticated
/// end to end while registration and sessions both carry credentials.</summary>
internal sealed class PluginPairingClient(IHttpClientFactory httpClientFactory)
{
	/// <summary>
	/// Creates a pairing request for a human to approve or reject in the Macro Deck desktop app.
	/// <paramref name="codeChallenge" /> is the PKCE challenge derived from a verifier only this process
	/// ever holds; the host stores only the challenge.
	/// </summary>
	public async Task<PluginPairingResponse> CreateAsync(
		string pluginId,
		string displayName,
		string codeChallenge,
		PluginPairingClientInfo? client,
		CancellationToken cancellationToken)
	{
		using var httpClient = Create();
		using var request = new HttpRequestMessage(HttpMethod.Post, ProtocolConstants.PairingPath)
		{
			Content = JsonContent.Create(new PluginPairingRequest
				{
					PluginId = pluginId,
					DisplayName = displayName,
					CodeChallenge = codeChallenge,
					CodeChallengeMethod = PluginPairingChallengeMethods.S256,
					Client = client
				},
				options: PluginProtocolJson.Options)
		};

		using var response = await httpClient.SendAsync(request, cancellationToken);

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			// A 404 here means the endpoint does not exist at all - an older host that predates pairing,
			// not a rejected request. Fatal regardless of what NotFound would otherwise imply, because a
			// host that has never heard of pairing will answer the exact same way on every retry.
			throw new PluginRegistrationException(
				"This host does not support interactive pairing. Use the headless enrollment fallback " +
				"described in the plugin authentication guide instead.",
				response.StatusCode,
				fatal: true);
		}

		if (response.StatusCode == HttpStatusCode.Forbidden)
		{
			throw DeveloperModeDisabled(response.StatusCode);
		}

		await ProtocolHttpHelpers.EnsureSuccessAsync(response, "start pairing", cancellationToken);

		return await ProtocolHttpHelpers.ReadAsync<PluginPairingResponse>(response, cancellationToken);
	}

	/// <summary>Polls the current status of a pairing request. An unknown or pruned id answers with
	/// status <see cref="PluginPairingStatuses.Expired" /> rather than a failure, by contract.</summary>
	public async Task<PluginPairingStatusResponse> GetStatusAsync(string requestId, CancellationToken cancellationToken)
	{
		using var httpClient = Create();
		using var response = await httpClient.GetAsync(
			$"{ProtocolConstants.PairingPath}/{Uri.EscapeDataString(requestId)}",
			cancellationToken);

		await ProtocolHttpHelpers.EnsureSuccessAsync(response, "check the pairing status", cancellationToken);

		return await ProtocolHttpHelpers.ReadAsync<PluginPairingStatusResponse>(response, cancellationToken);
	}

	/// <summary>
	/// Exchanges the code verifier for a plugin secret, once the request has been approved. Every
	/// failure mode - unknown, expired, not approved, already redeemed, wrong verifier - answers
	/// <see cref="HttpStatusCode.Unauthorized" /> identically, by contract, so this never learns which
	/// one happened.
	/// </summary>
	public async Task<PluginRegistrationResponse> RedeemAsync(
		string requestId,
		string codeVerifier,
		CancellationToken cancellationToken)
	{
		using var httpClient = Create();
		using var request = new HttpRequestMessage(HttpMethod.Post,
			$"{ProtocolConstants.PairingPath}/{Uri.EscapeDataString(requestId)}/redemption")
		{
			Content = JsonContent.Create(new PluginPairingRedemptionRequest { CodeVerifier = codeVerifier },
				options: PluginProtocolJson.Options)
		};

		using var response = await httpClient.SendAsync(request, cancellationToken);

		if (response.StatusCode == HttpStatusCode.Forbidden)
		{
			throw DeveloperModeDisabled(response.StatusCode);
		}

		await ProtocolHttpHelpers.EnsureSuccessAsync(response, "redeem the pairing request", cancellationToken);

		return await ProtocolHttpHelpers.ReadAsync<PluginRegistrationResponse>(response, cancellationToken);
	}

	private static PluginRegistrationException DeveloperModeDisabled(HttpStatusCode statusCode)
		// A bare 403 tells a developer nothing; naming the setting and where to flip it is the whole
		// point of catching this separately instead of letting EnsureSuccessAsync's generic message through.
		=> new("Interactive pairing needs Developer Mode enabled on the host. Enable it in the Macro Deck " +
			"desktop app's settings.",
			statusCode,
			fatal: true);

	private HttpClient Create()
	{
		var client = httpClientFactory.CreateClient(PluginRegistrationClient.HttpClientName);
		client.Timeout = ProtocolTimeouts.DefaultRequest;
		return client;
	}
}
