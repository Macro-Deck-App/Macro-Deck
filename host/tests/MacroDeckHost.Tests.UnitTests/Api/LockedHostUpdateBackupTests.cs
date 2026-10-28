using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Security.KeyRing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class LockedHostUpdateBackupTests
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

		KeyRingStartupState.Set(new KeyRingProtectionPlan(KeyRingProtectionMode.Locked,
				KeyRingLockReason.KeystoreEntryMissing,
				KeyRingUnprotectedReason.None,
				KeyRingBackend.MacOsKeychain,
				true,
				null,
				null,
				"deadbeef",
				false),
			new KeyRingKekHolder(),
			new FakeKekStore(),
			new KekStoreIdentity("Macro Deck Tests", "key-ring-kek"),
			false,
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
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
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
		KeyRingStartupState.Set(new KeyRingProtectionPlan(KeyRingProtectionMode.Unprotected,
				KeyRingLockReason.None,
				KeyRingUnprotectedReason.None,
				KeyRingBackend.None,
				false,
				null,
				null,
				null,
				false),
			new KeyRingKekHolder(),
			new NullKekStore("reset"),
			new KekStoreIdentity("Macro Deck", "key-ring-kek"),
			false,
			null);

		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task The_bootstrapper_is_told_the_backup_was_skipped_because_the_key_ring_is_locked()
	{
		var response = await PostBeforeHostUpdate(FakeConnectionShape.Loopback);
		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("skipped").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("reason").GetString(), Is.EqualTo("KeyRingLocked"));
			Assert.That(BackupFiles(), Is.Empty);
			Assert.That(await RecoveryKeySecretId(), Is.Null);
		});
	}

	[TestCase(FakeConnectionShape.PublicLan)]
	[TestCase(FakeConnectionShape.LoopbackWithoutCredential)]
	[TestCase(FakeConnectionShape.LoopbackOnPublicPort)]
	public async Task Only_the_trusted_loopback_caller_gets_an_answer(FakeConnectionShape shape)
	{
		var response = await PostBeforeHostUpdate(shape);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), shape.ToString());
			Assert.That(BackupFiles(), Is.Empty);
		});
	}

	private async Task<HttpResponseMessage> PostBeforeHostUpdate(FakeConnectionShape shape)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/backups/before-host-update");
		request.Content = JsonContent.Create(new { version = "3.0.1" });
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());

		return await _client.SendAsync(request);
	}

	private IEnumerable<string> BackupFiles()
	{
		return Directory.EnumerateFiles(_paths.BackupsDirectory, "*", SearchOption.AllDirectories);
	}

	private async Task<string?> RecoveryKeySecretId()
	{
		await using var scope = _host.Services.CreateAsyncScope();
		var preference = await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>()
			.GetByKey(AppPreferenceService.BackupRecoveryKeySecretIdKey);

		return preference?.Value;
	}
}
