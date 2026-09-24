using System.Security.Cryptography;
using MacroDeckHost.Infrastructure.Licensing;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

internal static class TestKeyLicenses
{
	// The public test-2026 key pair, trusted only in developer mode; the Platform's mock mode signs with it too.
	private const string PrivateKeyPem = """
		-----BEGIN PRIVATE KEY-----
		MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg3YdVZblUi2WgMWR5
		d8cvpwfE0pqjDpTNy2mFdENI6KehRANCAATHRT0yoORyVwLFQoDTde9TsghqK7MJ
		Yae8fIpDf7HeSC1ztGi+zVl7Gh+WUCqnmmc46l2q8+TCINOp4JRNR3nM
		-----END PRIVATE KEY-----
		""";

	public static string Sign(string? licenseId = null, DateTimeOffset? issuedAt = null)
	{
		var now = issuedAt ?? DateTimeOffset.UtcNow;
		using var key = ECDsa.Create();
		key.ImportFromPem(PrivateKeyPem);
		return CompanionLicenseTokens.Sign(key,
			CompanionLicenseTokens.TestKeyId,
			licenseId ?? Guid.NewGuid().ToString("N"),
			"google-play",
			now,
			now);
	}
}
