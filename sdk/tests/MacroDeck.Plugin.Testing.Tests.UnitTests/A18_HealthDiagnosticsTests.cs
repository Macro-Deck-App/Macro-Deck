using System.Net;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A18 - health, ready, info and diagnostics reflect real state; readiness is not liveness.
/// </summary>
[TestFixture]
public class A18_HealthDiagnosticsTests
{
	[Test]
	public async Task Health_ready_info_and_diagnostics_reflect_real_state()
	{
		var sessions = PluginSessionGate.Held();
		var options = new MacroDeckTestHostOptions { SessionCreation = sessions };
		await using var host = await MacroDeckTestHost.StartAsync(options);

		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable());
		await using var plugin = await host.LaunchAsync(spec);

		// Sessions are held for the whole host, so no session can exist yet no matter how fast the
		// plugin retries - before.Ready is therefore deterministic, not a race won by timing. That is
		// what the gate buys: without it, the test host accepts session creation immediately, so
		// before.Ready would only reflect how the handshake happened to race this probe, and a
		// ProbeHealthAsync that (incorrectly) reports Ready as simply "the process has not exited" would
		// win that race exactly as often as a correct implementation does.
		var before = await plugin.ProbeHealthAsync();

		Assert.Multiple(() =>
		{
			Assert.That(before.Live, Is.True);
			Assert.That(before.Ready, Is.False);
		});

		sessions.Release();
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var after = await HealthPolling.WaitUntilAsync(plugin, health => health.Ready);

		Assert.Multiple(() =>
		{
			Assert.That(after.Ready, Is.True);

			// The fixture's own manifest.json declares this id. LaunchAsync launches with
			// PluginTestCredentials.Managed, but peeks the target's manifest first and carries its id
			// through MACRO_DECK_PLUGIN_ID rather than inventing an unrelated one - the same thing a real
			// supervisor does (it launches with the id it read from the manifest it is activating), and
			// the only way a managed launch avoids PluginHostBuilder.Build's "manifest declares id X, but
			// configuration says Y" error for a target that already has one.
			Assert.That(after.Id, Is.EqualTo("app.macro-deck.well-behaved-test-plugin"));
			Assert.That(after.Name, Is.EqualTo("Well-Behaved Test Plugin"));
			Assert.That(after.Version, Is.EqualTo("1.0.0"));
			Assert.That(after.Mode, Is.EqualTo("Managed"));
			Assert.That(after.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
			Assert.That(after.DeclaredCapabilities, Is.EqualTo(session.Declared.Count));
			Assert.That(after.AcceptedCapabilities, Is.EqualTo(session.Accepted.Count));
		});

		using var client = new HttpClient { BaseAddress = plugin.BaseAddress };
		using var response = await client.GetAsync(new Uri("/_macrodeck/not-a-real-route", UriKind.Relative));
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}
}
