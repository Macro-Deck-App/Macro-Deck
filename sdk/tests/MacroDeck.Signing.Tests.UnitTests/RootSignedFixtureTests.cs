using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.Registry;

namespace MacroDeck.Signing.Tests.UnitTests;

[TestFixture]
internal sealed class RootSignedFixtureTests
{
	private static string FixturePath(params string[] parts) =>
		Path.Combine([AppContext.BaseDirectory, "signing-vectors", "v1-chain", .. parts]);

	private static byte[] FixtureRoot() =>
		Convert.FromBase64String(File.ReadAllText(FixturePath("root.public")).Trim());

	[Test]
	public async Task A_plugin_signed_before_issuer_certificates_existed_still_verifies()
	{
		var result = await PackageVerifier.VerifyAsync(FixturePath("plugin-v1.macroDeckPlugin"),
			new PluginManifestReader(),
			FixtureRoot());

		Assert.That(result.Success, Is.True, result.Message);
	}

	[Test]
	public async Task A_registry_snapshot_signed_before_issuer_certificates_existed_still_verifies()
	{
		var result = await RegistryManifestVerifier.VerifyAsync(FixturePath("registry", "registry-manifest.json"),
			FixturePath("registry", "registry-signature.json"),
			await File.ReadAllBytesAsync(FixturePath("registry-certificate.json")),
			await File.ReadAllBytesAsync(FixturePath("registry-certificate.sig")),
			FixtureRoot());

		Assert.That(result.Success, Is.True, result.Message);
	}
}
