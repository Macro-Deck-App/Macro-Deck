using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Licensing;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

internal sealed partial class CompanionLicenseServiceTests
{
	private static string PromoLicense(string licenseId = "license-promo")
		=> CompanionLicenseTokens.Sign(ProductionKey, ProductionKeyId, licenseId, "promo-code", Fixture.Now);

	[Test]
	public async Task A_redeemed_promo_code_stores_the_returned_license()
	{
		var fixture = new Fixture();
		var license = PromoLicense();
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Redeemed(license);
		var changes = 0;
		fixture.Service.LicenseChanged += (_, _) => changes++;

		var result = await fixture.Service.RedeemPromoCodeAsync("  ABCD-EFGH-JKMN-PQRS ", default);

		var status = await fixture.Service.GetStatusAsync(default);
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Redeemed));
			Assert.That(fixture.Accounts.RedeemLog, Is.EqualTo(new[] { "ABCD-EFGH-JKMN-PQRS" }));
			Assert.That(status.Licensed, Is.True);
			Assert.That(status.LicenseId, Is.EqualTo("license-promo"));
			Assert.That(status.Source, Is.EqualTo("promo-code"));
			Assert.That(changes, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_license_an_account_already_holds_is_stored_when_the_code_is_refused_for_it()
	{
		var fixture = new Fixture();
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.AccountLicenseExists(PromoLicense("license-account"));

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.AccountLicenseExists));
			Assert.That((await fixture.Service.GetStatusAsync(default)).LicenseId, Is.EqualTo("license-account"));
		});
	}

	[TestCase(PromoCodeRejection.Invalid, PromoCodeRedemptionStatus.Invalid)]
	[TestCase(PromoCodeRejection.Expired, PromoCodeRedemptionStatus.Expired)]
	[TestCase(PromoCodeRejection.AlreadyRedeemed, PromoCodeRedemptionStatus.AlreadyRedeemed)]
	[TestCase(PromoCodeRejection.Revoked, PromoCodeRedemptionStatus.Revoked)]
	public async Task A_rejected_promo_code_stores_nothing(PromoCodeRejection rejection, string expected)
	{
		var fixture = new Fixture();
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Rejected(rejection);

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(expected));
			Assert.That((await fixture.Service.GetStatusAsync(default)).Licensed, Is.False);
		});
	}

	[Test]
	public async Task A_rate_limited_redemption_reports_when_to_try_again()
	{
		var fixture = new Fixture();
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.RateLimited(TimeSpan.FromSeconds(90.2));

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.RateLimited));
			Assert.That(result.RetryAfterSeconds, Is.EqualTo(91));
		});
	}

	[Test]
	public async Task A_host_that_holds_a_purchased_license_does_not_spend_the_code()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, "license-bought") },
			default);
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Redeemed(PromoLicense());

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.AlreadyLicensed));
			Assert.That(fixture.Accounts.RedeemLog, Is.Empty);
			Assert.That((await fixture.Service.GetStatusAsync(default)).LicenseId, Is.EqualTo("license-bought"));
		});
	}

	[Test]
	public async Task A_promo_license_replaces_a_test_license()
	{
		var fixture = new Fixture();
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = TestKeyLicenses.Sign() }, default);
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Redeemed(PromoLicense());

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		var status = await fixture.Service.GetStatusAsync(default);
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Redeemed));
			Assert.That(status.IsTest, Is.False);
			Assert.That(status.LicenseId, Is.EqualTo("license-promo"));
		});
	}

	[Test]
	public async Task A_returned_license_this_host_cannot_verify_is_not_stored()
	{
		var fixture = new Fixture();
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Redeemed("not-a-token");

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Unavailable));
			Assert.That((await fixture.Service.GetStatusAsync(default)).Licensed, Is.False);
		});
	}

	[Test]
	public async Task A_redeemed_license_the_platform_has_revoked_is_not_stored()
	{
		var fixture = new Fixture();
		var revokedId = HexId(7);
		fixture.Repository.Values[CompanionLicenseService.PlatformRevokedIdsKey] =
			$$"""{ "Ids": ["{{revokedId}}"], "FetchedAt": 0 }""";
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Redeemed(PromoLicense(revokedId));

		var result = await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Revoked));
			Assert.That((await fixture.Service.GetStatusAsync(default)).Licensed, Is.False);
		});
	}

	[Test]
	public async Task A_pending_purchase_is_dropped_when_a_promo_license_arrives()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c", GooglePlay("purchase-a"), default);
		fixture.Accounts.RedeemAnswer = _ => new PlatformPromoCodeResult.Redeemed(PromoLicense());

		await fixture.Service.RedeemPromoCodeAsync("ABCD", default);

		var status = await fixture.Service.GetStatusAsync(default);
		Assert.Multiple(() =>
		{
			Assert.That(status.Licensed, Is.True);
			Assert.That(status.IssuePending, Is.False);
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public async Task A_blank_code_is_invalid_without_asking_the_platform(string? code)
	{
		var fixture = new Fixture();

		var result = await fixture.Service.RedeemPromoCodeAsync(code, default);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Invalid));
			Assert.That(fixture.Accounts.RedeemLog, Is.Empty);
		});
	}

	[Test]
	public async Task A_code_longer_than_any_real_one_is_invalid_without_asking_the_platform()
	{
		var fixture = new Fixture();

		var result = await fixture.Service.RedeemPromoCodeAsync(new string('A', CompanionLicenseService.MaximumPromoCodeLength + 1),
			default);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PromoCodeRedemptionStatus.Invalid));
			Assert.That(fixture.Accounts.RedeemLog, Is.Empty);
		});
	}
}
