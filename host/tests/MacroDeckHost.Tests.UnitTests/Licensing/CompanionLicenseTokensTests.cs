using System.Security.Cryptography;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

[TestFixture]
internal sealed class CompanionLicenseTokensTests
{
	private const string KeyId = "prod-test";

	private static readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
	private static readonly DateTimeOffset _issuedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private FakeTimeProvider _time = null!;
	private SwitchableKeyFactory _keys = null!;

	[SetUp]
	public void CreateProvider()
	{
		_time = new FakeTimeProvider();
		_keys = new SwitchableKeyFactory();
	}

	[OneTimeTearDown]
	public static void DisposeKey()
	{
		_signingKey.Dispose();
	}

	[Test]
	public void Constructing_the_verifier_needs_no_cryptography_provider()
	{
		_keys.Available = false;

		Assert.DoesNotThrow(() => Create());
		Assert.That(_keys.Created, Is.Zero);
	}

	[Test]
	public async Task An_unavailable_provider_is_reported_instead_of_an_invalid_license()
	{
		_keys.Available = false;
		var tokens = Create();

		Assert.That(tokens.VerificationAvailable, Is.False);
		Assert.ThrowsAsync<CompanionLicenseVerificationUnavailableException>(()
			=> tokens.VerifyAsync(License(), false));
		Assert.That(await tokens.VerifyAsync("", false), Is.Null);
		Assert.That(await tokens.VerifyAsync(null, false), Is.Null);
	}

	[Test]
	public async Task Verification_resumes_once_the_provider_is_back_and_announces_it_once()
	{
		_keys.Available = false;
		var tokens = Create();
		var restored = 0;
		tokens.VerificationRestored += (_, _) => restored++;
		Assert.That(tokens.VerificationAvailable, Is.False);

		_keys.Available = true;
		_time.Advance(CompanionLicenseTokens.UnavailableRetryDelay - TimeSpan.FromSeconds(1));
		Assert.That(tokens.VerificationAvailable, Is.False);

		_time.Advance(TimeSpan.FromSeconds(1));
		var license = await tokens.VerifyAsync(License(), false);
		await tokens.VerifyAsync(License(), false);

		Assert.That(license?.LicenseId, Is.EqualTo("license-1"));
		Assert.That(restored, Is.EqualTo(1));
	}

	[Test]
	public async Task A_provider_that_fails_after_the_keys_were_loaded_is_reported_and_fresh_keys_are_used_later()
	{
		var tokens = Create();
		Assert.That(await tokens.VerifyAsync(License(), false), Is.Not.Null);

		_keys.BreakCreatedKeys();
		_keys.Available = false;
		Assert.ThrowsAsync<CompanionLicenseVerificationUnavailableException>(()
			=> tokens.VerifyAsync(License(), false));

		_keys.Available = true;
		_time.Advance(CompanionLicenseTokens.UnavailableRetryDelay);

		Assert.That((await tokens.VerifyAsync(License(), false))?.LicenseId, Is.EqualTo("license-1"));
	}

	[Test]
	public async Task A_forged_license_is_invalid_while_the_provider_works()
	{
		var tokens = Create();
		using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var forged = CompanionLicenseTokens.Sign(otherKey, KeyId, "license-1", "google-play", _issuedAt);

		Assert.That(await tokens.VerifyAsync(forged, false), Is.Null);
		Assert.That(tokens.VerificationAvailable, Is.True);
	}

	private CompanionLicenseTokens Create()
	{
		var parameters = _signingKey.ExportParameters(false);
		var point = Convert.ToBase64String([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
		return new CompanionLicenseTokens(new Dictionary<string, string> { [KeyId] = point },
			Logger.None,
			_time,
			_keys.Create);
	}

	private static string License()
	{
		return CompanionLicenseTokens.Sign(_signingKey, KeyId, "license-1", "google-play", _issuedAt);
	}
}
