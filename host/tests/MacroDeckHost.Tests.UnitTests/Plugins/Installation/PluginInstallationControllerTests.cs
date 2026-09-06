using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Trust;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginInstallationControllerTests
{
	[TestCase(PluginTrustVerdict.Unsigned, "not_signed")]
	[TestCase(PluginTrustVerdict.VerificationUnavailable, "unverified")]
	[TestCase(PluginTrustVerdict.RevocationUnavailable, "unverified")]
	[TestCase(PluginTrustVerdict.Trusted, "valid")]
	[TestCase(PluginTrustVerdict.SignatureInvalid, "invalid")]
	[TestCase(PluginTrustVerdict.Malformed, "invalid")]
	[TestCase(PluginTrustVerdict.ContentMismatch, "invalid")]
	[TestCase(PluginTrustVerdict.UntrustedRoot, "invalid")]
	[TestCase(PluginTrustVerdict.WrongCertificatePurpose, "invalid")]
	[TestCase(PluginTrustVerdict.CertificateNotValidAtSignature, "invalid")]
	[TestCase(PluginTrustVerdict.Revoked, "invalid")]
	public async Task Inspect_reports_each_signature_verdict_under_its_own_wire_name(
		PluginTrustVerdict verdict,
		string expected)
	{
		var installer = new FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok("com.example.plugin", "1.0.0", null, activated: false)
				with
				{
					Signature = PluginTrustResult.Of(verdict, message: "why")
				}
		};

		var response = await Inspect(installer);

		Assert.Multiple(() =>
		{
			Assert.That(response.Signature?.Verification, Is.EqualTo(expected));
			Assert.That(response.Signature?.Message, Is.EqualTo("why"));
		});
	}

	[Test]
	public async Task Inspect_reports_no_verdict_when_nothing_was_verified()
	{
		var installer = new FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok("com.example.plugin", "1.0.0", null, activated: false)
		};

		var response = await Inspect(installer);

		Assert.Multiple(() =>
		{
			Assert.That(response.Signature, Is.Null);
			Assert.That(response.Publisher, Is.Null);
		});
	}

	[Test]
	public async Task Inspect_reports_the_publisher_the_installer_resolved()
	{
		var installer = new FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok("com.example.plugin", "1.0.0", null, activated: false)
				with
				{
					Publisher = new PluginPublisher
					{
						Name = "Acme Ltd",
						Id = "ltd.acme",
						Url = "https://acme.example"
					}
				}
		};

		var response = await Inspect(installer);

		Assert.Multiple(() =>
		{
			Assert.That(response.Publisher?.Name, Is.EqualTo("Acme Ltd"));
			Assert.That(response.Publisher?.Id, Is.EqualTo("ltd.acme"));
			Assert.That(response.Publisher?.Url, Is.EqualTo("https://acme.example"));
		});
	}

	[Test]
	public async Task Uninstall_keeps_plugin_data_unless_the_caller_asks_for_its_removal()
	{
		var installer = new FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok("com.example.plugin", "1.0.0", null, activated: false)
		};
		var controller = BuildController(installer);

		await controller.Uninstall("com.example.plugin", CancellationToken.None);
		var byDefault = installer.LastUninstallRequest;

		await controller.Uninstall("com.example.plugin", CancellationToken.None, keepData: false);
		var whenAsked = installer.LastUninstallRequest;

		Assert.Multiple(() =>
		{
			Assert.That(byDefault?.KeepData, Is.True);
			Assert.That(whenAsked?.KeepData, Is.False);
			Assert.That(byDefault?.Force, Is.False);
		});
	}

	private static async Task<PluginInstallActionResponse> Inspect(FakePluginInstaller installer)
	{
		var content = "artifact"u8.ToArray();
		using var stream = new MemoryStream(content);
		var file = new FormFile(stream, 0, content.Length, "file", "plugin.macroDeckPlugin");
		return await BuildController(installer).Inspect(file, CancellationToken.None);
	}

	private static PluginInstallationController BuildController(IPluginInstaller installer)
	{
		return new PluginInstallationController(installer,
			new FakeInstallationCatalog(),
			new PluginManifestReader(),
			new FakeArtifactCache(),
			new InMemoryPluginTrustRecordRepository());
	}
}
