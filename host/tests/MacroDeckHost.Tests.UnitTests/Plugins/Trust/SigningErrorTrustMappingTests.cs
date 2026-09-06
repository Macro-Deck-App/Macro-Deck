using MacroDeck.Signing;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Infrastructure.Plugins.Trust;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

[TestFixture]
internal sealed class SigningErrorTrustMappingTests
{
	[Test]
	public void Every_SigningError_member_maps_to_a_defined_verdict_and_none_maps_to_Trusted()
	{
		Assert.Multiple(() =>
		{
			foreach (var error in Enum.GetValues<SigningError>())
			{
				var verdict = error.ToVerdict();
				Assert.That(Enum.IsDefined(verdict), Is.True, $"{error} mapped to an undefined verdict.");
				Assert.That(verdict, Is.Not.EqualTo(PluginTrustVerdict.Trusted), $"{error} must never map to Trusted.");
			}
		});
	}

	[Test]
	public void Exactly_one_SigningError_member_maps_to_Unsigned_and_it_is_SignatureMissing()
	{
		var unsignedMembers = Enum.GetValues<SigningError>()
			.Where(error => error.ToVerdict() == PluginTrustVerdict.Unsigned)
			.ToList();

		Assert.That(unsignedMembers, Is.EqualTo(new[] { SigningError.SignatureMissing }));
	}

	// S17-style exhaustiveness: the table below is what actually gets asserted per-case, but SigningErrorTrustMapping
	// ends in a `_ => VerificationUnavailable` catch-all, so a member missing from this table would silently
	// inherit that verdict and never turn any of the per-case assertions red. Only comparing the table's key
	// set against the full enum - not merely checking each listed case - catches a newly added member that
	// nobody has classified.
	private static readonly Dictionary<SigningError, PluginTrustVerdict> _expectedVerdicts = new()
	{
		[SigningError.SignatureMissing] = PluginTrustVerdict.Unsigned,
		[SigningError.SignatureInvalid] = PluginTrustVerdict.SignatureInvalid,
		[SigningError.SignatureKeyIdMismatch] = PluginTrustVerdict.SignatureInvalid,
		[SigningError.SignatureMalformed] = PluginTrustVerdict.Malformed,
		[SigningError.CertificateMalformed] = PluginTrustVerdict.Malformed,
		[SigningError.CertificateUnreadable] = PluginTrustVerdict.Malformed,
		[SigningError.ManifestMissing] = PluginTrustVerdict.Malformed,
		[SigningError.ManifestMalformed] = PluginTrustVerdict.Malformed,
		[SigningError.ManifestTooLarge] = PluginTrustVerdict.Malformed,
		[SigningError.PackageFormatUnsupported] = PluginTrustVerdict.Malformed,
		[SigningError.CertificateUntrusted] = PluginTrustVerdict.UntrustedRoot,
		[SigningError.CertificateWrongPurpose] = PluginTrustVerdict.WrongCertificatePurpose,
		[SigningError.CertificateNotYetValid] = PluginTrustVerdict.CertificateNotValidAtSignature,
		[SigningError.CertificateExpired] = PluginTrustVerdict.CertificateNotValidAtSignature,
		[SigningError.FileDigestMismatch] = PluginTrustVerdict.ContentMismatch,
		[SigningError.FileSizeMismatch] = PluginTrustVerdict.ContentMismatch,
		[SigningError.UndeclaredFile] = PluginTrustVerdict.ContentMismatch,
		[SigningError.DeclaredFileMissing] = PluginTrustVerdict.ContentMismatch,
		[SigningError.UnsafeEntry] = PluginTrustVerdict.ContentMismatch,
		[SigningError.PackageUnreadable] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.SignatureAlgorithmUnsupported] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.PrivateKeyUnreadable] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.PrivateKeyMalformed] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.PrivateKeyDoesNotMatchCertificate] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.AlreadySigned] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.SelfVerificationFailed] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.OutputExists] = PluginTrustVerdict.VerificationUnavailable,
		[SigningError.WriteFailed] = PluginTrustVerdict.VerificationUnavailable
	};

	[Test]
	public void Every_SigningError_member_is_explicitly_classified_in_the_brief_s_table()
	{
		Assert.That(_expectedVerdicts.Keys,
			Is.EquivalentTo(Enum.GetValues<SigningError>()),
			"A SigningError member exists that this test does not classify.");

		Assert.Multiple(() =>
		{
			foreach (var (error, expected) in _expectedVerdicts)
			{
				Assert.That(error.ToVerdict(), Is.EqualTo(expected), $"{error} did not map to {expected}.");
			}
		});
	}
}
