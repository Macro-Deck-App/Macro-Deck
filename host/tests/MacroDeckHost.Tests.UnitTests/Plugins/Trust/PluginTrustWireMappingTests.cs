using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

[TestFixture]
internal sealed class PluginTrustWireMappingTests
{
	// S17: every PluginTrustVerdict member must appear here with its own classification. Adding a member
	// without adding it to this table fails the EquivalentTo assertion below, not just the coarse-mapping
	// one - a member that happened to inherit the correct coarse string by coincidence still fails here.
	private static readonly Dictionary<PluginTrustVerdict, string> _expectedCategories = new()
	{
		[PluginTrustVerdict.Unsigned] = "unsigned",
		[PluginTrustVerdict.Trusted] = "trusted",
		[PluginTrustVerdict.Malformed] = "malformed",
		[PluginTrustVerdict.SignatureInvalid] = "signature_invalid",
		[PluginTrustVerdict.ContentMismatch] = "content_mismatch",
		[PluginTrustVerdict.UntrustedRoot] = "untrusted_root",
		[PluginTrustVerdict.WrongCertificatePurpose] = "wrong_certificate_purpose",
		[PluginTrustVerdict.CertificateNotValidAtSignature] = "certificate_not_valid_at_signature",
		[PluginTrustVerdict.Revoked] = "revoked",
		[PluginTrustVerdict.RevocationUnavailable] = "revocation_unavailable",
		[PluginTrustVerdict.VerificationUnavailable] = "verification_unavailable"
	};

	[Test]
	public void S17_every_PluginTrustVerdict_member_is_explicitly_classified()
	{
		Assert.That(_expectedCategories.Keys,
			Is.EquivalentTo(Enum.GetValues<PluginTrustVerdict>()),
			"A PluginTrustVerdict member exists that this test does not classify.");

		Assert.Multiple(() =>
		{
			foreach (var (verdict, category) in _expectedCategories)
			{
				Assert.That(PluginInstallationController.ToTrustCategory(verdict), Is.EqualTo(category));
			}
		});
	}

	[Test]
	public void S44_every_verdict_maps_into_the_closed_wire_verification_set()
	{
		var closedSet = new[] { "not_signed", "unverified", "valid", "invalid" };

		Assert.Multiple(() =>
		{
			foreach (var verdict in Enum.GetValues<PluginTrustVerdict>())
			{
				Assert.That(closedSet,
					Does.Contain(PluginInstallationController.ToSignatureVerdict(verdict)),
					$"{verdict} mapped outside the closed set.");
			}
		});
	}

	[Test]
	public void S45_only_Trusted_maps_to_wire_valid()
	{
		Assert.Multiple(() =>
		{
			foreach (var verdict in Enum.GetValues<PluginTrustVerdict>())
			{
				var wire = PluginInstallationController.ToSignatureVerdict(verdict);
				if (verdict == PluginTrustVerdict.Trusted)
				{
					Assert.That(wire, Is.EqualTo("valid"));
				}
				else
				{
					Assert.That(wire, Is.Not.EqualTo("valid"), $"{verdict} must not map to valid.");
				}
			}

			var castFrom999 = (PluginTrustVerdict)999;
			Assert.That(PluginInstallationController.ToSignatureVerdict(castFrom999), Is.Not.EqualTo("valid"));
		});
	}

	[Test]
	public void S46_a_wrong_certificate_purpose_carries_coarse_invalid_and_the_fine_grained_category()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginInstallationController.ToSignatureVerdict(PluginTrustVerdict.WrongCertificatePurpose),
				Is.EqualTo("invalid"));
			Assert.That(PluginInstallationController.ToTrustCategory(PluginTrustVerdict.WrongCertificatePurpose),
				Is.EqualTo("wrong_certificate_purpose"));
		});
	}

	[Test]
	public void S47_every_PluginInstallError_maps_to_a_distinct_wire_code_and_none_of_the_new_ones_is_failed()
	{
		var codes = Enum.GetValues<PluginInstallError>()
			.Select(PluginInstallationController.ToErrorCode)
			.ToList();

		Assert.That(codes, Is.Unique);

		var newMembers = new[]
		{
			PluginInstallError.SignatureUntrusted,
			PluginInstallError.SignatureRevoked,
			PluginInstallError.SignatureUnverifiable,
			PluginInstallError.UnsignedNotPermitted,
			PluginInstallError.TrustDowngrade
		};

		Assert.Multiple(() =>
		{
			foreach (var error in newMembers)
			{
				Assert.That(PluginInstallationController.ToErrorCode(error), Is.Not.EqualTo("failed"));
			}
		});
	}
}
