using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A22 - interactive pairing (issue #588): a self-registering plugin with no enrollment token obtains
/// and persists its credential through <see cref="MacroDeckTestHost" />'s own pairing endpoints, and
/// those endpoints enforce proof of possession on redemption.
/// </summary>
[TestFixture]
public class A22_InteractivePairingTests
{
	[Test]
	public async Task Pairing_credentials_pair_interactively_and_persist_for_reuse_without_pairing_again()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var stateDirectory = new TempStateDirectory();

		const string pluginId = "test.a22.pairing";
		using var manifest = new PluginTestManifest(id: pluginId);
		var credentials = PluginTestCredentials.Pairing;

		PluginHostBuilder Build()
		{
			var builder = MacroDeckPlugin.CreatePlugin()
				.RegisterIntegration(_ => new TestIntegration(pluginId));

			builder.Configuration["MacroDeck:Plugin:StateDirectory"] = stateDirectory.Path;
			return builder;
		}

		await using (var firstRun = await host.HostAsync(Build(), credentials, manifest))
		{
			await host.WaitForSessionAsync();
			await firstRun.Application.StopAsync();
		}

		Assert.Multiple(() =>
		{
			Assert.That(host.PairingRequests, Has.Count.EqualTo(1));
			Assert.That(host.PairingRedemptions, Has.Count.EqualTo(1));

			// A pairing-issued registration must be indistinguishable from an enrollment-issued one to
			// the rest of the host - see MacroDeckTestHost.IssueSecret's own remarks.
			Assert.That(host.Registrations, Has.Count.EqualTo(1));
		});

		var challenge = host.PairingRequests.Single().CodeChallenge;
		var verifier = host.PairingRedemptions.Single().CodeVerifier;

		// Proof of possession, PKCE-style: the plugin never sends the same value twice, and the verifier
		// it later redeems with is exactly what the challenge it sent up front committed to.
		Assert.That(verifier, Is.Not.EqualTo(challenge));

		var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

		Assert.That(expectedChallenge, Is.EqualTo(challenge));
		Assert.That(CredentialFile.Read(stateDirectory.Path, pluginId),
			Is.Not.Null,
			"the credential pairing issued was never persisted");

		await using var secondRun = await host.HostAsync(Build(), credentials, manifest);
		await host.WaitForSessionAsync();
		var report = await HealthPolling.WaitUntilAsync(secondRun, health => health.Ready);

		Assert.Multiple(() =>
		{
			// The second start reused the persisted credential rather than pairing again.
			Assert.That(host.PairingRequests, Has.Count.EqualTo(1));
			Assert.That(report.Ready, Is.True);
			Assert.That(report.Mode, Is.EqualTo("SelfRegistering"));
		});
	}

	[Test]
	public async Task Redemption_rejects_a_wrong_verifier_and_accepts_the_correct_one()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var client = new HttpClient { BaseAddress = new Uri(host.Url) };

		var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
		var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

		using var createResponse = await client.PostAsJsonAsync(ProtocolConstants.PairingPath,
			new PluginPairingRequest
			{
				PluginId = "test.a22.redemption",
				DisplayName = "Redemption Test",
				CodeChallenge = challenge,
				CodeChallengeMethod = PluginPairingChallengeMethods.S256
			},
			PluginProtocolJson.Options);

		Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

		var created = await createResponse.Content.ReadFromJsonAsync<PluginPairingResponse>(PluginProtocolJson.Options);

		using var wrongResponse = await client.PostAsJsonAsync(
			$"{ProtocolConstants.PairingPath}/{created!.RequestId}/redemption",
			new PluginPairingRedemptionRequest { CodeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32)) },
			PluginProtocolJson.Options);

		Assert.That(wrongResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

		using var correctResponse = await client.PostAsJsonAsync(
			$"{ProtocolConstants.PairingPath}/{created.RequestId}/redemption",
			new PluginPairingRedemptionRequest { CodeVerifier = verifier },
			PluginProtocolJson.Options);

		Assert.That(correctResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

		var registration
			= await correctResponse.Content.ReadFromJsonAsync<PluginRegistrationResponse>(PluginProtocolJson.Options);

		Assert.That(registration!.PluginId, Is.EqualTo("test.a22.redemption"));
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
