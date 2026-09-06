using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A14 - both registration modes work, are distinguishable, and self-registration happens exactly once
/// across two starts against the same state directory.
/// </summary>
[TestFixture]
public class A14_RegistrationModeTests
{
	[Test]
	public async Task Managed_mode_never_registers_and_writes_no_state_file()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var stateDirectory = new TempStateDirectory();

		var credentials = PluginTestCredentials.Managed;

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration("test.a14.managed"));

		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = stateDirectory.Path;

		await using var plugin = await host.HostAsync(builder, credentials);
		await host.WaitForSessionAsync();

		// WaitForSessionAsync signals from the host's side of the handshake; the plugin updating its own
		// /_macrodeck/ready needs at least one more round trip, so this polls rather than asserting on a
		// single probe taken immediately after - see HealthPolling's own remarks.
		var report = await HealthPolling.WaitUntilAsync(plugin, health => health.Ready);

		Assert.Multiple(() =>
		{
			Assert.That(host.Registrations, Is.Empty);
			Assert.That(host.EnrollmentTokens, Is.Empty);
			Assert.That(report.Ready, Is.True);
			Assert.That(report.Mode, Is.EqualTo("Managed"));
		});

		// Managed mode never persists credentials - no file should appear under the id it was told to use.
		Assert.That(Directory.Exists(stateDirectory.Path) &&
			Directory.EnumerateFileSystemEntries(stateDirectory.Path).Any(),
			Is.False);
	}

	[Test]
	public async Task Self_registering_mode_registers_once_and_reuses_its_stored_secret_on_the_next_start()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var stateDirectory = new TempStateDirectory();
		using var otherStateDirectory = new TempStateDirectory();

		const string pluginId = "test.a14.self";
		var credentials = PluginTestCredentials.SelfRegistering;

		// Self-registering mode has no configured id to fall back on, so the manifest is what fixes the
		// plugin's identity across both starts - CredentialFile.Read below looks the persisted secret up
		// by this same id. One instance, reused for both builds: its content root is read-only once
		// Build() has run, and the two builds never run concurrently.
		using var manifest = new PluginTestManifest(id: pluginId);

		PluginHostBuilder BuildFirstRun()
		{
			var builder = MacroDeckPlugin.CreatePlugin()
				.RegisterIntegration(_ => new TestIntegration(pluginId));

			builder.Configuration["MacroDeck:Plugin:StateDirectory"] = stateDirectory.Path;
			return builder;
		}

		await using (var firstRun = await host.HostAsync(BuildFirstRun(), credentials, manifest))
		{
			await host.WaitForSessionAsync();
			await firstRun.Application.StopAsync();
		}

		var issued = CredentialFile.Read(stateDirectory.Path, pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(issued, Is.Not.Null, "the issued secret was never persisted");
			Assert.That(host.Registrations, Has.Count.EqualTo(1));

			// The only assertion separating "registered" from "registered with *this* token/secret":
			// without it, a library that presents an arbitrary or empty token, or persists a secret it
			// never actually got from this host, would still pass every assertion above.
			Assert.That(host.EnrollmentTokens.Single(),
				Is.EqualTo(credentials.EnrollmentToken),
				"the token presented at registration must be exactly the one issued to this identity");
			Assert.That(issued!.Secret,
				Is.EqualTo(host.IssuedSecrets.Single()),
				"the secret persisted to disk must be exactly the one this host issued");
		});

		// A second TempStateDirectory instance must not see the first one's file.
		Assert.That(CredentialFile.Read(otherStateDirectory.Path, pluginId), Is.Null);

		var secondBuilder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration(pluginId));

		secondBuilder.Configuration["MacroDeck:Plugin:StateDirectory"] = stateDirectory.Path;

		await using var secondRun = await host.HostAsync(secondBuilder, credentials, manifest);
		await host.WaitForSessionAsync();
		var report = await HealthPolling.WaitUntilAsync(secondRun, health => health.Ready);

		Assert.Multiple(() =>
		{
			// The second start reused the stored secret rather than registering again.
			Assert.That(host.Registrations, Has.Count.EqualTo(1));
			Assert.That(report.Mode, Is.EqualTo("SelfRegistering"));
		});
	}
}
