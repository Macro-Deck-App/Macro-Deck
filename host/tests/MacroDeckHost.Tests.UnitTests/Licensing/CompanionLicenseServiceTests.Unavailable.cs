using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Licensing;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

internal sealed partial class CompanionLicenseServiceTests
{
	[Test]
	public async Task The_status_says_licenses_cannot_be_verified_when_the_provider_is_unavailable()
	{
		var fixture = new Fixture();
		fixture.Keys.Available = false;

		var status = await fixture.Service.GetStatusAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(status.VerificationUnavailable, Is.True);
			Assert.That(status.Licensed, Is.False);
		});
	}

	[Test]
	public async Task A_stored_license_and_its_account_state_survive_an_unavailable_provider()
	{
		var fixture = new Fixture();
		var token = Sign(_productionKey, ProductionKeyId);
		fixture.Repository.Values[CompanionLicenseService.TokenKey] = token;
		fixture.Keys.Available = false;

		var status = await fixture.Service.GetStatusAsync(CancellationToken.None);
		await fixture.Service.SyncAsync("connection-a", GooglePlay("purchase-a", token), CancellationToken.None);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(status.VerificationUnavailable, Is.True);
			Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.EqualTo(token));
			Assert.That(fixture.Repository.Values.ContainsKey(CompanionLicenseService.AccountEligibleIdKey),
				Is.False);
			Assert.That(fixture.Repository.Values.ContainsKey(CompanionLicenseService.PendingProofsKey), Is.False);
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task No_promo_code_is_spent_while_licenses_cannot_be_verified(bool licenseStored)
	{
		var fixture = new Fixture();
		if (licenseStored)
		{
			fixture.Repository.Values[CompanionLicenseService.TokenKey] = Sign(_productionKey, ProductionKeyId);
		}

		fixture.Keys.Available = false;

		var result = await fixture.Service.RedeemPromoCodeAsync("PROMO", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Unavailable));
			Assert.That(fixture.Accounts.RedeemLog, Is.Empty);
		});
	}

	[Test]
	public async Task A_legacy_transfer_is_answered_at_once_without_being_queued_while_licenses_cannot_be_verified()
	{
		var fixture = new Fixture();
		fixture.Keys.Available = false;

		var result = await fixture.Service.TransferLegacyPurchaseAsync(Md2Purchase("app-transaction-1"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(LegacyPurchaseTransferStatus.Unavailable));
			Assert.That(fixture.Repository.Values.ContainsKey(CompanionLicenseService.PendingProofsKey), Is.False);
		});
	}

	[Test]
	public async Task A_companion_sync_still_gets_its_trial_start_while_licenses_cannot_be_verified()
	{
		var fixture = new Fixture();
		var request = GooglePlay("purchase-a");
		request.TrialDeviceId = "device-1";
		request.TrialStarted = true;
		var before = await fixture.Service.SyncAsync("connection-a", request, CancellationToken.None);
		fixture.Repository.Values.TryRemove(CompanionLicenseService.PendingProofsKey, out _);
		fixture.Keys.Available = false;
		fixture.Restart();

		var answer = await fixture.Service.SyncAsync("connection-a", request, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(answer.License, Is.Null);
			Assert.That(answer.TrialStartedAt, Is.EqualTo(before.TrialStartedAt).And.Not.Null);
			Assert.That(fixture.Repository.Values.ContainsKey(CompanionLicenseService.PendingProofsKey), Is.False);
		});
	}

	[Test]
	public async Task Background_work_waits_instead_of_issuing_while_licenses_cannot_be_verified()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("connection-a", GooglePlay("purchase-a"), CancellationToken.None);
		var pending = fixture.Repository.Values[CompanionLicenseService.PendingProofsKey];
		fixture.Keys.Available = false;
		fixture.Restart();

		var next = await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(next, Is.GreaterThan(fixture.Time.GetUtcNow()));
			Assert.That(fixture.Platform.Proofs, Is.Empty);
			Assert.That(fixture.Repository.Values[CompanionLicenseService.PendingProofsKey], Is.EqualTo(pending));
		});
	}

	[Test]
	public async Task An_issued_license_that_cannot_be_verified_keeps_its_proof_pending_instead_of_refusing_it()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("connection-a", GooglePlay("purchase-a"), CancellationToken.None);
		fixture.Platform.Answer = _ =>
		{
			fixture.Keys.BreakCreatedKeys();
			fixture.Keys.Available = false;
			return new PlatformLicenseIssueResult.Issued(Sign(_productionKey, ProductionKeyId));
		};

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Platform.Proofs, Has.Count.EqualTo(1));
			Assert.That(fixture.Repository.Values[CompanionLicenseService.PendingProofsKey],
				Does.Contain("\"ProofHash\""));
			Assert.That(fixture.Repository.Values.ContainsKey(CompanionLicenseService.RefusedProofKeysKey),
				Is.False);
			Assert.That(fixture.Repository.Values.ContainsKey(CompanionLicenseService.TokenKey), Is.False);
		});
	}

	[Test]
	public async Task The_stored_license_is_announced_again_once_the_provider_is_back()
	{
		var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-a", device);
		var token = Sign(_productionKey, ProductionKeyId);
		fixture.Repository.Values[CompanionLicenseService.TokenKey] = token;
		fixture.Keys.Available = false;
		await fixture.Service.GetStatusAsync(CancellationToken.None);
		var changed = 0;
		fixture.Service.LicenseChanged += (_, _) => changed++;

		fixture.Keys.Available = true;
		fixture.Time.Advance(CompanionLicenseTokens.UnavailableRetryDelay);
		var status = await fixture.Service.GetStatusAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseEvent)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(status.Licensed, Is.True);
			Assert.That(status.VerificationUnavailable, Is.False);
			Assert.That(changed, Is.GreaterThanOrEqualTo(1));
			Assert.That(pushes.Select(push => ((CompanionLicenseEvent)push.Message).License), Does.Contain(token));
		});
	}
}
