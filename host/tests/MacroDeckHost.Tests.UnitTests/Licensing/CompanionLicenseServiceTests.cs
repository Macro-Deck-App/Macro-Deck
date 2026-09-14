using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Licensing;
using MacroDeckHost.Tests.UnitTests.Companion;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Ui;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

[TestFixture]
internal sealed class CompanionLicenseServiceTests
{
	private const string ProductionKeyId = "prod-test-a";
	private const string SecondProductionKeyId = "prod-test-b";

	private static readonly ECDsa ProductionKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
	private static readonly ECDsa SecondProductionKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

	private static readonly IReadOnlyDictionary<string, string> TrustedKeys = new Dictionary<string, string>
	{
		[ProductionKeyId] = Point(ProductionKey), [SecondProductionKeyId] = Point(SecondProductionKey)
	};

	[OneTimeTearDown]
	public static void DisposeKeys()
	{
		ProductionKey.Dispose();
		SecondProductionKey.Dispose();
	}

	[Test]
	public async Task A_proof_is_answered_at_once_then_exchanged_stored_and_pushed()
	{
		var fixture = new Fixture();
		fixture.Platform = new GatedPlatform();
		var gated = (GatedPlatform)fixture.Platform;
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-other", device);

		var answer = await fixture.Service.SyncAsync("connection-buyer",
			new SyncCompanionLicenseRequest
			{
				Proof = new CompanionLicenseProof { Platform = "google-play", ProductId = CompanionLicenseTokens.Product }
			},
			CancellationToken.None);
		var answeredBeforeExchange = !fixture.Service.Exchange.IsCompleted;
		var token = Sign(ProductionKey, ProductionKeyId);
		gated.Issued.SetResult(token);
		await fixture.Service.Exchange.WaitAsync(TimeSpan.FromSeconds(5));

		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseEvent)
			.ToList();
		Assert.Multiple(async () =>
		{
			Assert.That(answer.License, Is.Null);
			Assert.That(answeredBeforeExchange, Is.True);
			Assert.That((await fixture.Service.GetStatusAsync(CancellationToken.None)).Licensed, Is.True);
			Assert.That(pushes.Select(push => push.ConnectionId),
				Is.EquivalentTo(new[] { "connection-other", "connection-buyer" }));
			Assert.That(pushes.Select(push => ((CompanionLicenseEvent)push.Message).License),
				Is.All.EqualTo(token));
		});
	}

	[Test]
	public async Task A_valid_submitted_token_is_stored_when_none_is_and_never_replaces_one()
	{
		var fixture = new Fixture();
		var first = Sign(ProductionKey, ProductionKeyId, "license-1");
		var second = Sign(ProductionKey, ProductionKeyId, "license-2");

		var adopted = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = first }, default);
		var kept = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = second }, default);
		var status = await fixture.Service.GetStatusAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(adopted.License, Is.EqualTo(first));
			Assert.That(kept.License, Is.EqualTo(first));
			Assert.That(status.LicenseId, Is.EqualTo("license-1"));
			Assert.That(status.Source, Is.EqualTo("google-play"));
			Assert.That(status.KeyId, Is.EqualTo(ProductionKeyId));
			Assert.That(status.IssuedAt, Is.EqualTo(Fixture.Now.ToUnixTimeMilliseconds()));
			Assert.That(status.IsTest, Is.False);
		});
	}

	[TestCase("tampered-payload")]
	[TestCase("tampered-signature")]
	[TestCase("unknown-kid")]
	[TestCase("key-a-labelled-b")]
	[TestCase("wrong-product")]
	[TestCase("garbage")]
	public async Task An_untrusted_token_is_rejected_and_not_stored(string kind)
	{
		var fixture = new Fixture();
		var valid = Sign(ProductionKey, ProductionKeyId);
		var parts = valid.Split('.');
		var token = kind switch
		{
			"tampered-payload" => $"{parts[0]}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
			{
				iss = CompanionLicenseTokens.Issuer, aud = CompanionLicenseTokens.Audience, sub = "forged",
				product = CompanionLicenseTokens.Product, source = "google-play", iat = 1
			}))}.{parts[2]}",
			"tampered-signature" => $"{parts[0]}.{parts[1]}.{Flip(parts[2])}",
			"unknown-kid" => Sign(ECDsa.Create(ECCurve.NamedCurves.nistP256), "prod-unknown"),
			"key-a-labelled-b" => Sign(ProductionKey, SecondProductionKeyId),
			"wrong-product" => WrongProduct(),
			_ => "not.a.token"
		};

		var answer = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = token }, default);

		Assert.Multiple(async () =>
		{
			Assert.That(answer.License, Is.Null);
			Assert.That((await fixture.Service.GetStatusAsync(default)).Licensed, Is.False);
		});
	}

	[Test]
	public async Task Every_trusted_production_kid_verifies_so_rotation_keeps_old_licenses()
	{
		var tokens = new CompanionLicenseTokens(TrustedKeys);

		var fromFirst = await tokens.VerifyAsync(Sign(ProductionKey, ProductionKeyId), trustTestKey: false);
		var fromSecond = await tokens.VerifyAsync(Sign(SecondProductionKey, SecondProductionKeyId), trustTestKey: false);

		Assert.Multiple(() =>
		{
			Assert.That(fromFirst?.KeyId, Is.EqualTo(ProductionKeyId));
			Assert.That(fromSecond?.KeyId, Is.EqualTo(SecondProductionKeyId));
		});
	}

	[Test]
	public async Task The_test_key_is_trusted_only_while_developer_mode_is_on()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);

		var refusedWhileOff = await fixture.Service.IssueTestLicenseAsync(default);
		fixture.Preferences.DeveloperMode = true;
		var issued = await fixture.Service.IssueTestLicenseAsync(default);
		var answeredWhileOn = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest(), default);
		fixture.Preferences.DeveloperMode = false;
		var answeredAfterOff = await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = answeredWhileOn.License },
			default);

		Assert.Multiple(async () =>
		{
			Assert.That(refusedWhileOff, Is.Null);
			Assert.That(issued?.IsTest, Is.True);
			Assert.That(issued?.Source, Is.EqualTo("test"));
			Assert.That(issued?.KeyId, Is.EqualTo(CompanionLicenseTokens.TestKeyId));
			Assert.That(answeredWhileOn.License, Is.Not.Null);
			Assert.That(answeredAfterOff.License, Is.Null);
			Assert.That((await fixture.Service.GetStatusAsync(default)).Licensed, Is.False);
		});
	}

	[Test]
	public async Task A_production_license_replaces_a_stored_test_license()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.IssueTestLicenseAsync(default);
		var production = Sign(ProductionKey, ProductionKeyId, "license-real");

		var answer = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = production }, default);
		var status = await fixture.Service.GetStatusAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(answer.License, Is.EqualTo(production));
			Assert.That(status.IsTest, Is.False);
			Assert.That(status.LicenseId, Is.EqualTo("license-real"));
		});
	}

	[Test]
	public async Task A_test_license_never_replaces_a_stored_production_license()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		var production = Sign(ProductionKey, ProductionKeyId, "license-real");
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = production }, default);

		var afterTest = await fixture.Service.IssueTestLicenseAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(afterTest?.IsTest, Is.False);
			Assert.That(afterTest?.LicenseId, Is.EqualTo("license-real"));
		});
	}

	[Test]
	public async Task A_proof_is_exchanged_while_only_a_test_license_is_stored()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.IssueTestLicenseAsync(default);
		var gated = new GatedPlatform();
		fixture.Platform = gated;

		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest
			{
				Proof = new CompanionLicenseProof { Platform = "google-play", ProductId = CompanionLicenseTokens.Product }
			},
			default);
		gated.Issued.SetResult(Sign(ProductionKey, ProductionKeyId, "license-bought"));
		await fixture.Service.Exchange.WaitAsync(TimeSpan.FromSeconds(5));

		var status = await fixture.Service.GetStatusAsync(default);
		Assert.Multiple(() =>
		{
			Assert.That(status.IsTest, Is.False);
			Assert.That(status.LicenseId, Is.EqualTo("license-bought"));
		});
	}

	[Test]
	public async Task Revoking_removes_a_stored_test_license_records_its_id_and_tells_every_companion()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		var issued = await fixture.Service.IssueTestLicenseAsync(default);
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-a", device);
		fixture.Preferences.DeveloperMode = false;

		var status = await fixture.Service.RevokeTestLicenseAsync(default);

		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseRevokedEvent)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(status.Licensed, Is.False);
			Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.Empty);
			Assert.That(fixture.StoredRevokedIds(), Is.EqualTo(new[] { issued!.LicenseId }));
			Assert.That(pushes.Select(push => push.ConnectionId), Is.EqualTo(new[] { "connection-a" }));
			Assert.That(pushes.Select(push => ((CompanionLicenseRevokedEvent)push.Message).LicenseId),
				Is.All.EqualTo(issued.LicenseId));
		});
	}

	[Test]
	public async Task A_stored_test_license_stays_revocable_after_developer_mode_is_turned_off()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.IssueTestLicenseAsync(default);
		fixture.Preferences.DeveloperMode = false;

		var status = await fixture.Service.GetStatusAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(status.Licensed, Is.False);
			Assert.That(status.TestLicenseStored, Is.True);
		});
	}

	[Test]
	public async Task Revoking_leaves_a_production_license_alone_and_tells_nobody()
	{
		var fixture = new Fixture();
		var production = Sign(ProductionKey, ProductionKeyId, "license-real");
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = production }, default);
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-a", device);

		var status = await fixture.Service.RevokeTestLicenseAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(status.LicenseId, Is.EqualTo("license-real"));
			Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.EqualTo(production));
			Assert.That(fixture.StoredRevokedIds(), Is.Empty);
			Assert.That(fixture.Harness.Transport.ConnectionMessages.Select(message => message.Message),
				Has.None.InstanceOf<CompanionLicenseRevokedEvent>());
		});
	}

	[Test]
	public async Task A_revoked_test_token_is_not_adopted_again_and_sync_names_it()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.IssueTestLicenseAsync(default);
		var held = (await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest(), default)).License;
		var revokedId = (await new CompanionLicenseTokens(TrustedKeys).VerifyAsync(held, trustTestKey: true))!.LicenseId;
		await fixture.Service.RevokeTestLicenseAsync(default);

		var answer = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = held }, default);
		var reissued = await fixture.Service.IssueTestLicenseAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(answer.License, Is.Null);
			Assert.That(answer.RevokedLicenseIds, Is.EqualTo(new[] { revokedId }));
			Assert.That(reissued?.IsTest, Is.True);
			Assert.That(reissued?.LicenseId, Is.Not.EqualTo(revokedId));
		});
	}

	[Test]
	public async Task The_revoked_list_is_capped_by_dropping_the_oldest_id()
	{
		var fixture = new Fixture();
		fixture.Platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);
		fixture.Preferences.DeveloperMode = true;
		var seeded = Enumerable.Range(0, CompanionLicenseService.MaximumRevokedTestIds).Select(index => $"seeded-{index}");
		await fixture.Repository.SetValue(CompanionLicenseService.RevokedTestIdsKey, JsonSerializer.Serialize(seeded));
		var issued = await fixture.Service.IssueTestLicenseAsync(default);

		await fixture.Service.RevokeTestLicenseAsync(default);

		var revoked = fixture.StoredRevokedIds();
		Assert.Multiple(() =>
		{
			Assert.That(revoked, Has.Count.EqualTo(CompanionLicenseService.MaximumRevokedTestIds));
			Assert.That(revoked, Does.Not.Contain("seeded-0"));
			Assert.That(revoked[0], Is.EqualTo("seeded-1"));
			Assert.That(revoked[^1], Is.EqualTo(issued!.LicenseId));
		});
	}

	[Test]
	public async Task The_fake_platform_issues_nothing_without_developer_mode()
	{
		var fixture = new Fixture();
		var platform = new FakePlatformLicenseClient(fixture.ScopeFactory, fixture.Time);

		var issued = await platform.IssueCompanionLicenseAsync(
			new CompanionLicenseProof { Platform = "app-store", ProductId = CompanionLicenseTokens.Product },
			default);

		Assert.That(issued, Is.Null);
	}

	[Test]
	public async Task The_host_records_its_own_time_for_a_trial_and_keeps_it()
	{
		var fixture = new Fixture();

		var unknown = await fixture.Service.SyncAsync("c", Trial("device-a", started: false), default);
		var started = await fixture.Service.SyncAsync("c", Trial("device-a", started: true), default);
		fixture.Time.Advance(TimeSpan.FromDays(3));
		var later = await fixture.Service.SyncAsync("c", Trial("device-a", started: true), default);

		Assert.Multiple(() =>
		{
			Assert.That(unknown.TrialStartedAt, Is.Null);
			Assert.That(started.TrialStartedAt, Is.EqualTo(Fixture.Now.ToUnixTimeMilliseconds()));
			Assert.That(later.TrialStartedAt, Is.EqualTo(Fixture.Now.ToUnixTimeMilliseconds()));
		});
	}

	[Test]
	public async Task Parallel_trial_starts_for_two_devices_both_persist()
	{
		var fixture = new Fixture();

		await Task.WhenAll(Enumerable.Range(0, 20)
			.Select(index => Task.Run(() =>
				fixture.Service.SyncAsync("c", Trial($"device-{index}", started: true), default))));

		Assert.That(fixture.StoredTrials().Keys, Has.Count.EqualTo(20));
	}

	[Test]
	public async Task The_trial_map_is_capped_by_dropping_the_oldest_start()
	{
		var fixture = new Fixture();
		var seeded = Enumerable.Range(0, CompanionLicenseService.MaximumTrials)
			.ToDictionary(index => $"seeded-{index}", index => (long)index);
		await fixture.Repository.SetValue(CompanionLicenseService.TrialsKey, JsonSerializer.Serialize(seeded));

		await fixture.Service.SyncAsync("c", Trial("newcomer", started: true), default);

		var trials = fixture.StoredTrials();
		Assert.Multiple(() =>
		{
			Assert.That(trials, Has.Count.EqualTo(CompanionLicenseService.MaximumTrials));
			Assert.That(trials, Does.Not.ContainKey("seeded-0"));
			Assert.That(trials, Does.ContainKey("seeded-1"));
			Assert.That(trials, Does.ContainKey("newcomer"));
		});
	}

	[Test]
	public async Task A_client_scope_device_syncs_over_the_websocket_and_cannot_supply_a_trial_start()
	{
		var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Phone");
		using var dispatcher = CompanionStateAndActionsTests.Dispatcher(fixture.Harness,
			new ClaimsPrincipal(new ClaimsIdentity(
				[
					new Claim(AuthDefaults.DeviceClaim, device.ToString()),
					new Claim(AuthDefaults.ScopeClaim, AuthDefaults.ClientScope)
				],
				"test")),
			fixture.Service);

		var response = await dispatcher.DispatchAsync("SyncCompanionLicense",
			CompanionStateAndActionsTests.Payload(new
			{
				license = (string?)null, proof = (object?)null, trialDeviceId = "device-a", trialStarted = true,
				trialStartedAt = 0
			}),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response!.Value.GetProperty("license").ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(response.Value.GetProperty("revokedLicenseIds").GetArrayLength(), Is.Zero);
			Assert.That(response.Value.GetProperty("trialStartedAt").GetInt64(),
				Is.EqualTo(Fixture.Now.ToUnixTimeMilliseconds()));
		});
	}

	[Test]
	public void A_sync_from_a_token_without_a_device_claim_is_refused()
	{
		var fixture = new Fixture();
		using var dispatcher = CompanionStateAndActionsTests.Dispatcher(fixture.Harness,
			new ClaimsPrincipal(new ClaimsIdentity([], "test")),
			fixture.Service);

		var refusal = Assert.ThrowsAsync<UiWebSocketDispatchException>(async () =>
			await dispatcher.DispatchAsync("SyncCompanionLicense",
				CompanionStateAndActionsTests.Payload(new { trialDeviceId = "device-a", trialStarted = true }),
				CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(refusal!.Code, Is.EqualTo("forbidden"));
			Assert.That(fixture.StoredTrials(), Is.Empty);
		});
	}

	private static SyncCompanionLicenseRequest Trial(string deviceId, bool started)
		=> new() { TrialDeviceId = deviceId, TrialStarted = started };

	private static string Sign(ECDsa key, string keyId, string licenseId = "license-1")
		=> CompanionLicenseTokens.Sign(key, keyId, licenseId, "google-play", Fixture.Now);

	private static string WrongProduct()
	{
		var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
		return handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
		{
			Issuer = CompanionLicenseTokens.Issuer,
			Audience = CompanionLicenseTokens.Audience,
			IssuedAt = Fixture.Now.UtcDateTime,
			Claims = new Dictionary<string, object> { ["sub"] = "x", ["product"] = "other", ["source"] = "test" },
			SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
				new Microsoft.IdentityModel.Tokens.ECDsaSecurityKey(ProductionKey) { KeyId = ProductionKeyId },
				Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256)
		});
	}

	private static string Point(ECDsa key)
	{
		var parameters = key.ExportParameters(false);
		return Convert.ToBase64String([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static string Flip(string signature)
		=> (signature[0] == 'A' ? 'B' : 'A') + signature[1..];

	private sealed class Fixture
	{
		public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

		private readonly ForwardingPlatform _platform = new();

		public Fixture()
		{
			var services = new ServiceCollection();
			services.AddSingleton<IAppPreferenceRepository>(Repository);
			services.AddSingleton<IAppPreferenceService>(Preferences);
			ScopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
			Service = new CompanionLicenseService(ScopeFactory,
				_platform,
				new CompanionLicenseTokens(TrustedKeys),
				Harness.DeviceRegistry,
				Time,
				new LoggerConfiguration().CreateLogger());
		}

		public CompanionHarness Harness { get; } = new();
		public MemoryPreferences Repository { get; } = new();
		public FakeDeveloperModePreferences Preferences { get; } = new();
		public FakeTimeProvider Time { get; } = new() { Now = Now };
		public IServiceScopeFactory ScopeFactory { get; }
		public CompanionLicenseService Service { get; }

		public IPlatformLicenseClient Platform
		{
			get => _platform.Inner;
			set => _platform.Inner = value;
		}

		public Dictionary<string, long> StoredTrials()
			=> Repository.Values.TryGetValue(CompanionLicenseService.TrialsKey, out var json)
				? JsonSerializer.Deserialize<Dictionary<string, long>>(json)!
				: [];

		public List<string> StoredRevokedIds()
			=> Repository.Values.TryGetValue(CompanionLicenseService.RevokedTestIdsKey, out var json)
				? JsonSerializer.Deserialize<List<string>>(json)!
				: [];
	}

	private sealed class ForwardingPlatform : IPlatformLicenseClient
	{
		public IPlatformLicenseClient Inner { get; set; } = new GatedPlatform();

		public Task<string?> IssueCompanionLicenseAsync(CompanionLicenseProof proof, CancellationToken cancellationToken)
			=> Inner.IssueCompanionLicenseAsync(proof, cancellationToken);
	}

	private sealed class GatedPlatform : IPlatformLicenseClient
	{
		public TaskCompletionSource<string?> Issued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<string?> IssueCompanionLicenseAsync(CompanionLicenseProof proof, CancellationToken cancellationToken)
			=> Issued.Task;
	}

	internal sealed class MemoryPreferences : IAppPreferenceRepository
	{
		public ConcurrentDictionary<string, string> Values { get; } = new();

		public async Task<AppPreferenceEntity?> GetByKey(string key)
		{
			await Task.Yield();
			return Values.TryGetValue(key, out var value) ? new AppPreferenceEntity { Key = key, Value = value } : null;
		}

		public async Task SetValue(string key, string value)
		{
			await Task.Yield();
			Values[key] = value;
		}
	}
}
