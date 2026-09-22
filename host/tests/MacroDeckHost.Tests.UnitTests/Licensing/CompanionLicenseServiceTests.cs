using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Licensing;
using MacroDeckHost.Tests.UnitTests.Companion;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.DataProtection;
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
	public async Task A_proof_is_answered_at_once_then_issued_in_the_background_stored_and_pushed()
	{
		var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-other", device);
		var token = Sign(ProductionKey, ProductionKeyId);
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(token);

		var answer = await fixture.Service.SyncAsync("connection-buyer", GooglePlay("purchase-a"), CancellationToken.None);
		var callsBeforeWork = fixture.Platform.Proofs.Count;
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseEvent)
			.ToList();
		var status = await fixture.Service.GetStatusAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(answer.License, Is.Null);
			Assert.That(callsBeforeWork, Is.Zero);
			Assert.That(fixture.Platform.Proofs.Select(proof => proof.PurchaseToken), Is.EqualTo(new[] { "purchase-a" }));
			Assert.That(status.Licensed, Is.True);
			Assert.That(status.IssuePending, Is.False);
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
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.IssueTestLicenseAsync(default);
		var production = Sign(ProductionKey, ProductionKeyId, "license-bought");
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(production);

		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		await fixture.Service.RunDueWorkAsync(default);

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
	public async Task The_test_issuer_issues_nothing_without_developer_mode()
	{
		var fixture = new Fixture();

		var issued = await new TestCompanionLicenseIssuer(fixture.ScopeFactory, fixture.Time).IssueAsync(default);

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

	[Test]
	public async Task A_transient_failure_keeps_the_proof_pending_and_retries_with_growing_delays()
	{
		var fixture = new Fixture();
		var token = Sign(ProductionKey, ProductionKeyId);
		var failures = 3;
		fixture.Platform.Answer = _ => failures-- > 0
			? new PlatformLicenseIssueResult.Retry(null, false)
			: new PlatformLicenseIssueResult.Issued(token);
		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);

		var delays = new List<TimeSpan>();
		var pendingSeen = new List<bool>();
		for (var attempt = 0; attempt < 3; attempt++)
		{
			await fixture.Service.RunDueWorkAsync(default);
			var status = await fixture.Service.GetStatusAsync(default);
			pendingSeen.Add(status.IssuePending);
			var next = DateTimeOffset.FromUnixTimeMilliseconds(status.NextIssueAttemptAt!.Value);
			delays.Add(next - fixture.Time.Now);
			await fixture.Service.RunDueWorkAsync(default);
			fixture.Time.Now = next;
		}

		var callsBeforeRecovery = fixture.Platform.Proofs.Count;
		await fixture.Service.RunDueWorkAsync(default);
		var recovered = await fixture.Service.GetStatusAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(pendingSeen, Is.All.True);
			Assert.That(callsBeforeRecovery, Is.EqualTo(3), "a retry never runs before its time");
			Assert.That(delays[0], Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)).And.LessThanOrEqualTo(TimeSpan.FromSeconds(10)));
			Assert.That(delays[2], Is.GreaterThan(delays[0]));
			Assert.That(recovered.Licensed, Is.True);
			Assert.That(recovered.IssuePending, Is.False);
			Assert.That(recovered.NextIssueAttemptAt, Is.Null);
		});
	}

	[Test]
	public void The_retry_delay_starts_at_a_few_seconds_is_jittered_capped_at_thirty_minutes_and_honours_retry_after()
	{
		var fixture = new Fixture();
		fixture.RandomValue = 0;
		var firstLow = fixture.Service.RetryDelay(1, null);
		var manyLow = fixture.Service.RetryDelay(5000, null);
		fixture.RandomValue = 1;
		var firstHigh = fixture.Service.RetryDelay(1, null);
		var manyHigh = fixture.Service.RetryDelay(int.MaxValue, null);
		var requested = fixture.Service.RetryDelay(1, TimeSpan.FromMinutes(10));
		var excessive = fixture.Service.RetryDelay(1, TimeSpan.FromHours(5));

		Assert.Multiple(() =>
		{
			Assert.That(firstLow, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
			Assert.That(firstHigh, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(10)));
			Assert.That(firstLow, Is.Not.EqualTo(firstHigh), "jitter");
			Assert.That(manyLow, Is.GreaterThan(TimeSpan.FromMinutes(5)).And.LessThanOrEqualTo(TimeSpan.FromMinutes(30)));
			Assert.That(manyHigh, Is.EqualTo(TimeSpan.FromMinutes(30)));
			Assert.That(requested, Is.EqualTo(TimeSpan.FromMinutes(10)));
			Assert.That(excessive, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(30)));
		});
	}

	[Test]
	public async Task A_pending_proof_survives_a_restart_and_is_encrypted_at_rest()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c", GooglePlay("secret-purchase-token"), default);
		await fixture.Service.RunDueWorkAsync(default);
		var stored = string.Join("\n", fixture.Repository.Values.Values);
		var token = Sign(ProductionKey, ProductionKeyId);
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(token);

		var restarted = fixture.Restart();
		var pendingAfterRestart = (await restarted.GetStatusAsync(default)).IssuePending;
		fixture.Time.Advance(TimeSpan.FromMinutes(1));
		await restarted.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(stored, Does.Not.Contain("secret-purchase-token"));
			Assert.That(stored, Does.Not.Contain("GPA.1234"));
			Assert.That(pendingAfterRestart, Is.True);
			Assert.That(fixture.Platform.Proofs.Last().PurchaseToken, Is.EqualTo("secret-purchase-token"));
			Assert.That((await restarted.GetStatusAsync(default)).Licensed, Is.True);
		});
	}

	[Test]
	public async Task A_refusal_stops_retrying_and_the_same_purchase_is_not_sent_again_even_after_a_restart()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Refused("purchase-refunded");

		await fixture.Service.SyncAsync("c", AppStore("2000001", "1000001"), default);
		await fixture.Service.RunDueWorkAsync(default);
		var afterRefusal = await fixture.Service.GetStatusAsync(default);
		var restarted = fixture.Restart();
		await restarted.SyncAsync("c", AppStore("2000002", "1000001"), default);
		fixture.Time.Advance(TimeSpan.FromHours(1));
		await restarted.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(afterRefusal.IssuePending, Is.False);
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(1));
			Assert.That((await restarted.GetStatusAsync(default)).IssuePending, Is.False);
		});
	}

	[TestCase("invalid-signature")]
	[TestCase("bundle-mismatch")]
	public async Task A_refused_proof_is_not_sent_again_but_a_different_proof_for_the_purchase_is(string code)
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = proof => proof.TransactionId == "2000001"
			? new PlatformLicenseIssueResult.Refused(code)
			: new PlatformLicenseIssueResult.Issued(Sign(ProductionKey, ProductionKeyId));

		await fixture.Service.SyncAsync("c", AppStore("2000001", "1000001"), default);
		await fixture.Service.RunDueWorkAsync(default);
		await fixture.Service.SyncAsync("c", AppStore("2000001", "1000001"), default);
		await fixture.Service.RunDueWorkAsync(default);
		var callsForTheRefusedProof = fixture.Platform.Proofs.Count;
		var restarted = fixture.Restart();
		await restarted.SyncAsync("c", AppStore("2000002", "1000001"), default);
		await restarted.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(callsForTheRefusedProof, Is.EqualTo(1));
			Assert.That((await restarted.GetStatusAsync(default)).Licensed, Is.True);
		});
	}

	[TestCase("package-mismatch")]
	[TestCase("sandbox-purchase")]
	public async Task A_google_play_proof_refused_for_a_platform_side_reason_is_tried_again_after_a_day(string code)
	{
		var fixture = new Fixture();
		var answers = new Queue<PlatformLicenseIssueResult>([
			new PlatformLicenseIssueResult.Refused(code),
			new PlatformLicenseIssueResult.Issued(Sign(ProductionKey, ProductionKeyId))
		]);
		fixture.Platform.Answer = _ => answers.Dequeue();

		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		await fixture.Service.RunDueWorkAsync(default);
		fixture.Time.Advance(TimeSpan.FromHours(2));
		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		await fixture.Service.RunDueWorkAsync(default);
		var callsWithinADay = fixture.Platform.Proofs.Count;
		var restarted = fixture.Restart();
		fixture.Time.Advance(TimeSpan.FromDays(1));
		await restarted.SyncAsync("c", GooglePlay("purchase-a"), default);
		await restarted.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(callsWithinADay, Is.EqualTo(1));
			Assert.That((await restarted.GetStatusAsync(default)).Licensed, Is.True);
		});
	}

	[Test]
	public async Task A_full_refused_list_drops_a_one_day_block_before_a_refunded_purchase()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = proof => new PlatformLicenseIssueResult.Refused(
			proof.PurchaseToken!.StartsWith("refunded", StringComparison.Ordinal) ? "purchase-refunded" : "package-mismatch");
		await fixture.Service.SyncAsync("c", GooglePlay("refunded-0"), default);
		await fixture.Service.RunDueWorkAsync(default);

		for (var index = 0; index < CompanionLicenseService.MaximumRefusedProofKeys; index++)
		{
			await fixture.Service.SyncAsync("c", GooglePlay($"misconfigured-{index}"), default);
			await fixture.Service.RunDueWorkAsync(default);
		}

		var calls = fixture.Platform.Proofs.Count;
		await fixture.Service.SyncAsync("c", GooglePlay("refunded-0"), default);
		await fixture.Service.RunDueWorkAsync(default);

		Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(calls));
	}

	[Test]
	public async Task Two_app_store_proofs_of_one_purchase_share_one_pending_entry()
	{
		var fixture = new Fixture();

		await fixture.Service.SyncAsync("c", AppStore("2000001", "1000001"), default);
		await fixture.Service.SyncAsync("d", AppStore("2000002", "1000001"), default);
		await fixture.Service.RunDueWorkAsync(default);

		Assert.That(fixture.Platform.Proofs.Select(proof => proof.TransactionId), Is.EqualTo(new[] { "2000002" }));
	}

	[Test]
	public async Task A_different_purchase_is_still_issued_while_another_stays_pending_at_the_store()
	{
		var fixture = new Fixture();
		var token = Sign(ProductionKey, ProductionKeyId, "license-b");
		fixture.Platform.Answer = proof => proof.PurchaseToken == "purchase-b"
			? new PlatformLicenseIssueResult.Issued(token)
			: new PlatformLicenseIssueResult.Retry(null, true);

		await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default);
		await fixture.Service.RunDueWorkAsync(default);
		await fixture.Service.SyncAsync("b", GooglePlay("purchase-b"), default);
		await fixture.Service.RunDueWorkAsync(default);

		var status = await fixture.Service.GetStatusAsync(default);
		Assert.Multiple(() =>
		{
			Assert.That(status.LicenseId, Is.EqualTo("license-b"));
			Assert.That(status.IssuePending, Is.False);
		});
	}

	[Test]
	public async Task A_purchase_the_store_never_completes_is_dropped_after_a_week()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Retry(null, true);
		await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default);
		await fixture.Service.RunDueWorkAsync(default);
		var pendingAfterADay = false;

		for (var day = 1; day <= 8; day++)
		{
			fixture.Time.Advance(TimeSpan.FromDays(1));
			await fixture.Service.RunDueWorkAsync(default);
			if (day == 1)
			{
				pendingAfterADay = (await fixture.Service.GetStatusAsync(default)).IssuePending;
			}
		}

		var calls = fixture.Platform.Proofs.Count;
		await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default);
		await fixture.Service.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(pendingAfterADay, Is.True);
			Assert.That((await fixture.Service.GetStatusAsync(default)).IssuePending, Is.False);
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(calls));
		});
	}

	[Test]
	public async Task A_sync_during_an_attempt_is_answered_at_once_and_a_new_purchase_stays_pending()
	{
		var fixture = new Fixture();
		fixture.Platform.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default);
		var run = fixture.Service.RunDueWorkAsync(default);
		await fixture.Platform.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var sameAnswer = await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default)
			.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.Service.SyncAsync("b", GooglePlay("purchase-b"), default).WaitAsync(TimeSpan.FromSeconds(5));
		fixture.Platform.Gate.SetResult();
		await run.WaitAsync(TimeSpan.FromSeconds(5));

		var pending = JsonSerializer.Deserialize<List<JsonElement>>(
			fixture.Repository.Values[CompanionLicenseService.PendingProofsKey])!;
		Assert.Multiple(() =>
		{
			Assert.That(sameAnswer.License, Is.Null);
			Assert.That(pending, Has.Count.EqualTo(2));
			Assert.That(fixture.Platform.Proofs.Count(proof => proof.PurchaseToken == "purchase-a"), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_license_the_platform_revokes_is_dropped_every_companion_is_told_and_it_is_never_taken_back()
	{
		var fixture = new Fixture();
		var revokedId = HexId(7);
		var production = Sign(ProductionKey, ProductionKeyId, revokedId);
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = production }, default);
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-a", device);
		fixture.Platform.RevokedIds = [revokedId, HexId(8)];

		await fixture.Service.RunDueWorkAsync(default);
		var answer = await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = production }, default);

		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseRevokedEvent)
			.ToList();
		Assert.Multiple(async () =>
		{
			Assert.That((await fixture.Service.GetStatusAsync(default)).Licensed, Is.False);
			Assert.That(answer.License, Is.Null);
			Assert.That(answer.RevokedLicenseIds, Is.EquivalentTo(new[] { revokedId, HexId(8) }));
			Assert.That(pushes.Select(push => push.ConnectionId), Is.EqualTo(new[] { "connection-a" }));
			Assert.That(pushes.Select(push => ((CompanionLicenseRevokedEvent)push.Message).LicenseId),
				Is.All.EqualTo(revokedId));
		});
	}

	[Test]
	public async Task The_cached_revocation_list_still_applies_while_the_platform_is_unreachable()
	{
		var fixture = new Fixture();
		var revokedId = HexId(7);
		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, HexId(1)) },
			default);
		fixture.Platform.RevokedIds = [revokedId];
		await fixture.Service.RunDueWorkAsync(default);
		fixture.Platform.RevokedIds = null;
		var restarted = fixture.Restart();
		await fixture.Repository.SetValue(CompanionLicenseService.TokenKey,
			Sign(ProductionKey, ProductionKeyId, revokedId));

		await restarted.RunDueWorkAsync(default);
		var answer = await restarted.SyncAsync("c", new SyncCompanionLicenseRequest(), default);

		Assert.Multiple(() =>
		{
			Assert.That(answer.License, Is.Null);
			Assert.That(answer.RevokedLicenseIds, Is.EqualTo(new[] { revokedId }));
		});
	}

	[Test]
	public async Task A_host_without_any_companion_never_asks_the_platform_for_revocations()
	{
		var fixture = new Fixture();

		await fixture.Service.RunDueWorkAsync(default);
		var withoutCompanion = fixture.Platform.RevocationFetches;
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest(), default);
		await fixture.Service.RunDueWorkAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(withoutCompanion, Is.Zero);
			Assert.That(fixture.Platform.RevocationFetches, Is.EqualTo(1));
		});
	}

	[TestCase(4000, true)]
	[TestCase(10_000, false)]
	public async Task A_sync_answer_carries_the_platform_list_and_always_fits_in_one_websocket_message(int count,
		bool complete)
	{
		var fixture = new Fixture();
		var held = HexId(3);
		fixture.Platform.RevokedIds = Enumerable.Range(0, count).Select(HexId).ToList();
		await fixture.Repository.SetValue(CompanionLicenseService.RevokedTestIdsKey,
			JsonSerializer.Serialize(Enumerable.Range(0, CompanionLicenseService.MaximumRevokedTestIds)
				.Select(index => Guid.NewGuid().ToString("N"))));
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest(), default);
		await fixture.Service.RunDueWorkAsync(default);
		var stored = Sign(ProductionKey, ProductionKeyId, HexId(count + 1), billingId: new string('b', 256));
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = stored }, default);

		var answer = await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, held) },
			default);
		var bytes = JsonSerializer.SerializeToUtf8Bytes(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
				"response",
				"SyncCompanionLicense",
				Guid.NewGuid().ToString(),
				Guid.NewGuid().ToString(),
				answer,
				null),
			UiWebSocketProtocol.Json);

		Assert.Multiple(() =>
		{
			Assert.That(bytes.Length, Is.LessThan(UiWebSocketProtocol.MaxMessageBytes));
			Assert.That(answer.RevokedLicenseIds, Does.Contain(held));
			Assert.That(answer.RevokedLicenseIds.Count(id => id.All(Uri.IsHexDigit) && id.Length == 32 && id != held),
				complete ? Is.GreaterThanOrEqualTo(count - 1) : Is.EqualTo(CompanionLicenseService.MaximumRevokedTestIds));
		});
	}

	[Test]
	public async Task Purchase_details_are_shown_when_present_and_a_malformed_one_only_hides_the_detail()
	{
		var fixture = new Fixture();
		var purchasedAt = Fixture.Now.AddDays(-2);
		var detailed = Sign(ProductionKey, ProductionKeyId, "license-1", purchasedAt, "GPA.3344-5566");
		var malformed = SignWithClaims(new Dictionary<string, object>
		{
			["sub"] = "license-2", ["product"] = CompanionLicenseTokens.Product, ["source"] = "app-store",
			["purchased_at"] = "yesterday", ["billing_id"] = 42
		});

		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = detailed }, default);
		var status = await fixture.Service.GetStatusAsync(default);
		var tokens = new CompanionLicenseTokens(TrustedKeys);
		var malformedLicense = await tokens.VerifyAsync(malformed, trustTestKey: false);

		Assert.Multiple(() =>
		{
			Assert.That(status.PurchasedAt, Is.EqualTo(purchasedAt.ToUnixTimeSeconds() * 1000));
			Assert.That(status.BillingId, Is.EqualTo("GPA.3344-5566"));
			Assert.That(status.LicenseId, Is.EqualTo("license-1"));
			Assert.That(malformedLicense, Is.Not.Null);
			Assert.That(malformedLicense!.PurchasedAt, Is.Null);
			Assert.That(malformedLicense.BillingId, Is.Null);
		});
	}

	[Test]
	public async Task The_license_page_is_told_about_every_change_and_never_sees_the_token()
	{
		var fixture = new Fixture();
		var token = Sign(ProductionKey, ProductionKeyId, HexId(5));
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(token);

		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		var afterQueue = ChangeEvents(fixture);
		await fixture.Service.RunDueWorkAsync(default);
		var afterIssue = ChangeEvents(fixture);
		var status = JsonSerializer.Serialize(await fixture.Service.GetStatusAsync(default));

		Assert.Multiple(() =>
		{
			Assert.That(afterQueue, Is.GreaterThan(0));
			Assert.That(afterIssue, Is.GreaterThan(afterQueue));
			Assert.That(status, Does.Not.Contain(token.Split('.')[2]));
		});
	}

	[Test]
	public async Task Nothing_secret_reaches_the_log()
	{
		var fixture = new Fixture();
		var token = Sign(ProductionKey, ProductionKeyId, HexId(5));
		var answers = new Queue<PlatformLicenseIssueResult>([
			new PlatformLicenseIssueResult.Retry(null, false),
			new PlatformLicenseIssueResult.Refused("purchase-refunded"),
			new PlatformLicenseIssueResult.Issued(token)
		]);
		fixture.Platform.Answer = _ => answers.Dequeue();

		await fixture.Service.SyncAsync("c", GooglePlay("secret-token-a"), default);
		await fixture.Service.RunDueWorkAsync(default);
		fixture.Time.Advance(TimeSpan.FromMinutes(1));
		await fixture.Service.RunDueWorkAsync(default);
		await fixture.Service.SyncAsync("c", AppStore("2000001", "1000001"), default);
		await fixture.Service.RunDueWorkAsync(default);
		fixture.Platform.RevokedIds = [HexId(5)];
		fixture.Time.Advance(TimeSpan.FromHours(2));
		await fixture.Service.RunDueWorkAsync(default);

		var logged = string.Join("\n", fixture.Sink.Events.Select(logEvent =>
			logEvent.RenderMessage(CultureInfo.InvariantCulture) + string.Join(",", logEvent.Properties.Values)));
		Assert.Multiple(() =>
		{
			Assert.That(fixture.Sink.Events, Is.Not.Empty);
			Assert.That(logged, Does.Not.Contain("secret-token-a"));
			Assert.That(logged, Does.Not.Contain(token.Split('.')[1]));
			Assert.That(logged, Does.Not.Contain("c2lnbmF0dXJl"));
			Assert.That(logged, Does.Not.Contain("GPA.1234"));
		});
	}

	[Test]
	public async Task The_worker_retries_a_proof_left_pending_before_a_restart_and_fetches_revocations()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(Sign(ProductionKey, ProductionKeyId));
		var restarted = fixture.Restart();
		var worker = Worker(fixture, restarted);

		await worker.StartAsync(default);
		var licensed = await Eventually(async () => (await restarted.GetStatusAsync(default)).Licensed);
		await worker.StopAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(licensed, Is.True);
			Assert.That(fixture.Platform.RevocationFetches, Is.GreaterThanOrEqualTo(1));
		});
	}

	[Test]
	public async Task A_new_proof_wakes_the_worker_without_waiting_for_a_timer()
	{
		var fixture = new Fixture();
		var worker = Worker(fixture, fixture.Service);
		await worker.StartAsync(default);

		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		var called = await Eventually(() => Task.FromResult(!fixture.Platform.Proofs.IsEmpty));
		await worker.StopAsync(default);

		Assert.That(called, Is.True);
	}

	[Test]
	public async Task A_failing_pass_is_logged_and_the_worker_keeps_going()
	{
		var fixture = new Fixture();
		var token = Sign(ProductionKey, ProductionKeyId);
		var calls = 0;
		fixture.Platform.Answer = _ => Interlocked.Increment(ref calls) == 1
			? throw new InvalidOperationException("boom")
			: new PlatformLicenseIssueResult.Issued(token);
		var worker = Worker(fixture, fixture.Service);
		await worker.StartAsync(default);

		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		var licensed = await Eventually(async () =>
		{
			fixture.Time.Advance(CompanionLicenseBackgroundService.FailureDelay);
			return (await fixture.Service.GetStatusAsync(default)).Licensed;
		});
		await worker.StopAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(licensed, Is.True);
			Assert.That(fixture.Sink.Events.Select(logEvent => logEvent.Exception), Has.Some.InstanceOf<InvalidOperationException>());
		});
	}

	[Test]
	public async Task A_macro_deck_2_purchase_is_transferred_stored_and_pushed_to_connected_companions()
	{
		var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-other", device);
		var token = Sign(ProductionKey, ProductionKeyId);
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(token);
		var proof = Md2Purchase("app-transaction-1");

		var result = await TransferAsync(fixture, proof);

		var sent = fixture.Platform.Proofs.Single();
		var status = await fixture.Service.GetStatusAsync(default);
		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseEvent)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Transferred)));
			Assert.That(sent.Platform, Is.EqualTo("app-store-legacy"));
			Assert.That(sent.LegacyKind, Is.EqualTo("appTransaction"));
			Assert.That(sent.ProductId, Is.EqualTo(CompanionLicenseTokens.Product));
			Assert.That(sent.SignedPayload, Is.EqualTo(proof.SignedPayload));
			Assert.That(status.Licensed, Is.True);
			Assert.That(status.IssuePending, Is.False);
			Assert.That(pushes.Select(push => push.ConnectionId), Is.EqualTo(new[] { "connection-other" }));
			Assert.That(pushes.Select(push => ((CompanionLicenseEvent)push.Message).License), Is.All.EqualTo(token));
		});
	}

	[Test]
	public async Task A_host_that_already_holds_a_license_answers_already_transferred_without_asking_the_platform()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId) },
			default);

		var result = await fixture.Service.TransferLegacyPurchaseAsync(Md2Purchase("app-transaction-1"), default);

		Assert.Multiple(() =>
		{
			Assert.That(result,
				Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.AlreadyTransferred)));
			Assert.That(fixture.Platform.Proofs, Is.Empty);
		});
	}

	[Test]
	public async Task A_refused_purchase_is_rejected_with_the_platform_code_and_asking_again_does_not_reach_the_platform()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Refused("purchase-before-paid-period");
		var proof = Md2Purchase("app-transaction-1");

		var first = await TransferAsync(fixture, proof);
		var second = await fixture.Service.TransferLegacyPurchaseAsync(proof, default).WaitAsync(TimeSpan.FromSeconds(5));

		var expected = new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Rejected, "purchase-before-paid-period");
		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(expected));
			Assert.That(second, Is.EqualTo(expected));
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_platform_that_cannot_decide_yet_leaves_the_purchase_pending_with_its_code()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Retry(null, false, "store-unavailable");

		var result = await TransferAsync(fixture, Md2Purchase("app-transaction-1"));

		Assert.Multiple(async () =>
		{
			Assert.That(result,
				Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Pending, "store-unavailable")));
			Assert.That((await fixture.Service.GetStatusAsync(default)).IssuePending, Is.True);
		});
	}

	[Test]
	public async Task A_transfer_the_platform_does_not_answer_within_ten_seconds_is_reported_pending()
	{
		var fixture = new Fixture();
		fixture.Platform.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var transfer = fixture.Service.TransferLegacyPurchaseAsync(Md2Purchase("app-transaction-1"), default);
		await WaitingTransfers(fixture, 1);
		var run = fixture.Service.RunDueWorkAsync(default);
		await fixture.Platform.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
		fixture.Time.Advance(CompanionLicenseService.LegacyTransferWait);
		var result = await transfer.WaitAsync(TimeSpan.FromSeconds(5));
		fixture.Platform.Gate.SetResult();
		await run.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(result, Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Pending)));
	}

	[Test]
	public async Task Proofs_carrying_the_same_app_transaction_id_are_one_pending_purchase()
	{
		var fixture = new Fixture();

		await TransferAsync(fixture, Md2Purchase("app-transaction-1", signedDate: "1"));
		await TransferAsync(fixture, Md2Purchase("app-transaction-1", signedDate: "2"));

		Assert.Multiple(() =>
		{
			Assert.That(PendingEntries(fixture), Has.Count.EqualTo(1));
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task Transfers_never_push_a_companion_proof_out_of_the_queue()
	{
		var fixture = new Fixture();
		for (var i = 0; i < CompanionLicenseService.MaximumPendingProofs; i++)
		{
			await fixture.Service.SyncAsync("c", GooglePlay($"purchase-{i}"), default);
		}

		var result = await fixture.Service.TransferLegacyPurchaseAsync(Md2Purchase("app-transaction-1"), default)
			.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Unavailable)));
			Assert.That(PendingEntries(fixture), Has.Count.EqualTo(CompanionLicenseService.MaximumPendingProofs));
			Assert.That(PendingEntries(fixture).Count(entry => entry.GetProperty("LegacyApp").GetBoolean()), Is.Zero);
		});
	}

	[Test]
	public async Task A_third_waiting_transfer_replaces_the_oldest_transfer_and_keeps_the_companion_proof()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);

		await TransferAsync(fixture, Md2Purchase("app-transaction-1"));
		fixture.Time.Advance(TimeSpan.FromSeconds(1));
		await TransferAsync(fixture, Md2Purchase("app-transaction-2"));
		fixture.Time.Advance(TimeSpan.FromSeconds(1));
		await TransferAsync(fixture, Md2Purchase("app-transaction-3"));

		var entries = PendingEntries(fixture);
		Assert.Multiple(() =>
		{
			Assert.That(entries, Has.Count.EqualTo(1 + CompanionLicenseService.MaximumPendingLegacyAppProofs));
			Assert.That(entries.Count(entry => !entry.GetProperty("LegacyApp").GetBoolean()), Is.EqualTo(1));
			Assert.That(entries.Min(entry => entry.GetProperty("QueuedAt").GetInt64()),
				Is.EqualTo(Fixture.Now.ToUnixTimeMilliseconds()));
			Assert.That(entries.Where(entry => entry.GetProperty("LegacyApp").GetBoolean())
					.Min(entry => entry.GetProperty("QueuedAt").GetInt64()),
				Is.EqualTo(Fixture.Now.AddSeconds(1).ToUnixTimeMilliseconds()));
		});
	}

	[Test]
	public async Task When_one_purchase_is_transferred_another_waiting_purchase_is_already_transferred()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(Sign(ProductionKey, ProductionKeyId));

		var first = fixture.Service.TransferLegacyPurchaseAsync(Md2Purchase("app-transaction-1"), default);
		await WaitingTransfers(fixture, 1);
		var second = fixture.Service.TransferLegacyPurchaseAsync(Md2Purchase("app-transaction-2"), default);
		await WaitingTransfers(fixture, 2);
		await fixture.Service.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(5)),
				Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Transferred)));
			Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)),
				Is.EqualTo(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.AlreadyTransferred)));
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Two_transfers_of_the_same_purchase_at_once_both_get_the_result()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(Sign(ProductionKey, ProductionKeyId));
		var proof = Md2Purchase("app-transaction-1");

		var first = fixture.Service.TransferLegacyPurchaseAsync(proof, default);
		var second = fixture.Service.TransferLegacyPurchaseAsync(proof, default);
		await WaitingTransfers(fixture, 2);
		await fixture.Service.RunDueWorkAsync(default);

		var expected = new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Transferred);
		Assert.Multiple(async () =>
		{
			Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(expected));
			Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(expected));
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task An_identical_proof_resent_during_an_attempt_that_is_refused_is_not_sent_again()
	{
		var fixture = new Fixture();
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Refused("invalid-signature");
		fixture.Platform.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default);
		var run = fixture.Service.RunDueWorkAsync(default);
		await fixture.Platform.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

		await fixture.Service.SyncAsync("a", GooglePlay("purchase-a"), default).WaitAsync(TimeSpan.FromSeconds(5));
		fixture.Platform.Gate.SetResult();
		await run.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.Service.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(1));
			Assert.That((await fixture.Service.GetStatusAsync(default)).IssuePending, Is.False);
		});
	}

	[Test]
	public async Task A_companion_cannot_submit_a_macro_deck_2_purchase()
	{
		var fixture = new Fixture();

		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { Proof = Md2Purchase("app-transaction-1") },
			default);
		await fixture.Service.RunDueWorkAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That(fixture.Platform.Proofs, Is.Empty);
			Assert.That((await fixture.Service.GetStatusAsync(default)).IssuePending, Is.False);
		});
	}

	private static async Task<LegacyPurchaseTransferResult> TransferAsync(Fixture fixture, CompanionLicenseProof proof)
	{
		var transfer = fixture.Service.TransferLegacyPurchaseAsync(proof, default);
		await WaitingTransfers(fixture, 1);
		await fixture.Service.RunDueWorkAsync(default);
		return await transfer.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static async Task WaitingTransfers(Fixture fixture, int count)
		=> Assert.That(await Eventually(() => Task.FromResult(fixture.Time.ActiveTimerCount >= count)), Is.True);

	private static List<JsonElement> PendingEntries(Fixture fixture)
		=> JsonSerializer.Deserialize<List<JsonElement>>(fixture.Repository.Values[CompanionLicenseService.PendingProofsKey])!;

	private static CompanionLicenseProof Md2Purchase(string appTransactionId, string signedDate = "1")
		=> new()
		{
			Platform = "app-store-legacy", LegacyKind = "appTransaction", ProductId = CompanionLicenseTokens.Product,
			SignedPayload = $"eyJhbGciOiJFUzI1NiJ9.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
			{
				appTransactionId, bundleId = "com.suchbyte.macrodeck", signedDate
			}))}.c2lnbmF0dXJl"
		};

	private static CompanionLicenseBackgroundService Worker(Fixture fixture, CompanionLicenseService service)
		=> new(service, fixture.Time, new LoggerConfiguration().WriteTo.Sink(fixture.Sink).CreateLogger());

	private static async Task<bool> Eventually(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (await condition())
			{
				return true;
			}

			await Task.Delay(20);
		}

		return false;
	}

	private static int ChangeEvents(Fixture fixture)
		=> fixture.Harness.Transport.GroupMessages.Count(message =>
			message.Group == UiAdminGroups.Admin && message.Message is CompanionLicenseChangedEvent);

	private static SyncCompanionLicenseRequest GooglePlay(string purchaseToken, string? license = null)
		=> new()
		{
			License = license,
			Proof = new CompanionLicenseProof
			{
				Platform = "google-play", ProductId = CompanionLicenseTokens.Product, PurchaseToken = purchaseToken,
				PackageName = "app.macrodeck.companion", OrderId = "GPA.1234"
			}
		};

	private static SyncCompanionLicenseRequest AppStore(string transactionId, string originalTransactionId)
		=> new()
		{
			Proof = new CompanionLicenseProof
			{
				Platform = "app-store", ProductId = CompanionLicenseTokens.Product, TransactionId = transactionId,
				SignedPayload = $"eyJhbGciOiJFUzI1NiJ9.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
				{
					transactionId, originalTransactionId, productId = CompanionLicenseTokens.Product
				}))}.c2lnbmF0dXJl"
			}
		};

	private static string HexId(int index) => index.ToString("x32", CultureInfo.InvariantCulture);

	private static SyncCompanionLicenseRequest Trial(string deviceId, bool started)
		=> new() { TrialDeviceId = deviceId, TrialStarted = started };

	private static string Sign(ECDsa key,
		string keyId,
		string licenseId = "license-1",
		DateTimeOffset? purchasedAt = null,
		string? billingId = null)
		=> CompanionLicenseTokens.Sign(key, keyId, licenseId, "google-play", Fixture.Now, purchasedAt, billingId);

	private static string SignWithClaims(Dictionary<string, object> claims)
		=> new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }
			.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
			{
				Issuer = CompanionLicenseTokens.Issuer,
				Audience = CompanionLicenseTokens.Audience,
				IssuedAt = Fixture.Now.UtcDateTime,
				Claims = claims,
				SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
					new Microsoft.IdentityModel.Tokens.ECDsaSecurityKey(ProductionKey) { KeyId = ProductionKeyId },
					Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256)
			});

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

		public Fixture()
		{
			var services = new ServiceCollection();
			services.AddSingleton<IAppPreferenceRepository>(Repository);
			services.AddSingleton<IAppPreferenceService>(Preferences);
			ScopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
			Service = Create();
		}

		public CompanionHarness Harness { get; } = new();
		public MemoryPreferences Repository { get; } = new();
		public FakeDeveloperModePreferences Preferences { get; } = new();
		public FakeTimeProvider Time { get; } = new() { Now = Now };
		public ScriptedPlatform Platform { get; } = new();
		public EphemeralDataProtectionProvider Protection { get; } = new();
		public CompanionHarness.CapturingSink Sink { get; } = new();
		public double RandomValue { get; set; } = 0.5;
		public IServiceScopeFactory ScopeFactory { get; }
		public CompanionLicenseService Service { get; private set; }

		public CompanionLicenseService Restart()
		{
			Service.Dispose();
			Service = Create();
			return Service;
		}

		public Dictionary<string, long> StoredTrials()
			=> Repository.Values.TryGetValue(CompanionLicenseService.TrialsKey, out var json)
				? JsonSerializer.Deserialize<Dictionary<string, long>>(json)!
				: [];

		public List<string> StoredRevokedIds()
			=> Repository.Values.TryGetValue(CompanionLicenseService.RevokedTestIdsKey, out var json)
				? JsonSerializer.Deserialize<List<string>>(json)!
				: [];

		private CompanionLicenseService Create()
			=> new(ScopeFactory,
				Platform,
				new TestCompanionLicenseIssuer(ScopeFactory, Time),
				new CompanionLicenseTokens(TrustedKeys),
				Harness.DeviceRegistry,
				Harness.Transport,
				Protection,
				Time,
				() => RandomValue,
				new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(Sink).CreateLogger());
	}

	internal sealed class ScriptedPlatform : IPlatformLicenseClient
	{
		public ConcurrentQueue<CompanionLicenseProof> Proofs { get; } = new();

		public Func<CompanionLicenseProof, PlatformLicenseIssueResult> Answer { get; set; } =
			_ => new PlatformLicenseIssueResult.Retry(null, false);

		public TaskCompletionSource? Gate { get; set; }

		public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public IReadOnlyList<string>? RevokedIds { get; set; } = [];

		public int RevocationFetches;

		public async Task<PlatformLicenseIssueResult> IssueCompanionLicenseAsync(CompanionLicenseProof proof,
			CancellationToken cancellationToken)
		{
			Proofs.Enqueue(proof);
			Called.TrySetResult();
			if (Gate is { } gate)
			{
				await gate.Task.WaitAsync(cancellationToken);
			}

			return Answer(proof);
		}

		public Task<IReadOnlyList<string>?> GetRevokedLicenseIdsAsync(CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref RevocationFetches);
			return Task.FromResult(RevokedIds);
		}
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
