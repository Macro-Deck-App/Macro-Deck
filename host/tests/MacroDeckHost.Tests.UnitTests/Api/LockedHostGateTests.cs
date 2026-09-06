using System.Net;
using System.Security.Cryptography;
using System.Net.Http.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Infrastructure.Auth;
using Microsoft.AspNetCore.DataProtection;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using MacroDeckHost.Tests.UnitTests.Security.KeyRing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

/// <summary>
/// A host whose key ring is wrapped but unopenable must run without touching anything it cannot read.
/// The failure this guards is silent: Data Protection answers an unreadable ring by minting a
/// replacement, so a locked host that carried on normally would orphan every stored secret and look
/// healthy doing it.
/// </summary>
[NonParallelizable]
public class LockedHostGateTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);

		File.WriteAllText(Path.Combine(_paths.KeysDirectory, "auth-signing.key"), "original");

		KeyRingStartupState.Set(LockedPlan(),
			new KeyRingKekHolder(),
			new FakeKekStore(),
			new KekStoreIdentity("Macro Deck Tests", "key-ring-kek"),
			portable: false,
			Path.Combine(_dataDir, "scratch-keys"));
		Directory.CreateDirectory(KeyRingStartupState.ScratchKeysDirectory!);

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		KeyRingStartupState.Set(UnlockedPlan(),
			new KeyRingKekHolder(),
			new NullKekStore("reset"),
			new KekStoreIdentity("Macro Deck", "key-ring-kek"),
			portable: false,
			null);

		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task An_ordinary_api_call_is_refused_while_the_key_ring_is_locked()
	{
		var response = await _client.GetAsync(new Uri("/api/folders", UriKind.Relative));

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
			Assert.That(response.Headers.Contains("X-MacroDeck-Locked"), Is.True);
		});
	}

	// The gate itself has to render, and it is reached without a session because the token signing key
	// is part of what is unreadable.
	[TestCase("/api/key-ring/status")]
	[TestCase("/api/auth/status")]
	public async Task The_endpoints_the_gate_needs_answer_without_a_session(string path)
	{
		var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
	}

	// The bootstrapper waits on these and gives up with a blocking error dialog if the host never
	// answers. Whether they then require a session is the usual authorization question and is not the
	// gate's business - what matters is that locking does not silence them.
	[TestCase("/api/system/version")]
	[TestCase("/api/host/shutdown")]
	[TestCase("/api/localization")]
	public async Task The_bootstrappers_probes_are_not_silenced_by_the_lock(string path)
	{
		var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

		Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable), path);
	}

	[Test]
	public async Task The_status_endpoint_reports_the_lock_without_needing_a_token()
	{
		var status = await _client.GetFromJsonAsync<LockedStatus>(new Uri("/api/key-ring/status", UriKind.Relative));

		Assert.Multiple(() =>
		{
			Assert.That(status!.Locked, Is.True);
			Assert.That(status.LockReason, Is.EqualTo(nameof(KeyRingLockReason.KeystoreEntryMissing)));
		});
	}

	// The whole point of the scratch directory and DisableAutomaticKeyGeneration. Data Protection
	// answers a ring it cannot read by quietly minting a replacement, so this drives it the way the
	// running host would - asking for a protector - rather than asserting an absence nothing would have
	// disturbed. Remove either defence and the real keys directory gains a file here.
	[Test]
	public void Asking_a_locked_host_to_protect_something_never_touches_the_real_key_ring()
	{
		var provider = _host.Services.GetRequiredService<IDataProtectionProvider>();

		// CryptographicException specifically: that is the failure every existing Data Protection
		// consumer in the host already handles, so a locked host degrades the way they expect.
		Assert.Throws<CryptographicException>(() =>
			provider.CreateProtector("MacroDeck.Secrets").Protect([1, 2, 3]));

		Assert.Multiple(() =>
		{
			Assert.That(Directory.GetFiles(_paths.KeysDirectory, KeyRingFileInspector.KeyFilePattern),
				Is.Empty,
				"a locked host created a key ring file");
			Assert.That(File.ReadAllText(Path.Combine(_paths.KeysDirectory, "auth-signing.key")),
				Is.EqualTo("original"),
				"a locked host overwrote the auth signing key");
		});
	}

	// The signing key provider regenerates over its own file whenever it cannot unprotect it, which
	// during a lock would destroy the real key. A locked host must not be resolving that one at all.
	[Test]
	public void A_locked_host_uses_a_signing_key_that_never_reaches_disk()
	{
		var provider = _host.Services.GetRequiredService<ISigningKeyProvider>();

		Assert.Multiple(() =>
		{
			Assert.That(provider, Is.InstanceOf<EphemeralSigningKeyProvider>());
			Assert.That(provider.GetKey(), Has.Length.EqualTo(64));
			Assert.That(File.ReadAllText(Path.Combine(_paths.KeysDirectory, "auth-signing.key")),
				Is.EqualTo("original"));
		});
	}

	private static KeyRingProtectionPlan LockedPlan()
		=> new(KeyRingProtectionMode.Locked,
			KeyRingLockReason.KeystoreEntryMissing,
			KeyRingUnprotectedReason.None,
			KeyRingBackend.MacOsKeychain,
			true,
			null,
			null,
			"deadbeef",
			false);

	private static KeyRingProtectionPlan UnlockedPlan()
		=> new(KeyRingProtectionMode.Unprotected,
			KeyRingLockReason.None,
			KeyRingUnprotectedReason.None,
			KeyRingBackend.None,
			false,
			null,
			null,
			null,
			false);

	private sealed record LockedStatus(bool Locked, string LockReason);
}
