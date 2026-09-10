using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Network.Tls;

[TestFixture]
public class PublicTlsCertificateValidatorTests
{
	[Test]
	public void A_Certificate_And_Its_Own_Key_Validate_And_Report_Matching_Metadata()
	{
		var (certificatePem, privateKeyPem, certificate) = GenerateSelfSignedPem("CN=validator-own-key.test");

		var result = PublicTlsCertificateValidator.Validate(certificatePem,
			privateKeyPem,
			PublicTlsCertificateSource.Custom);

		Assert.Multiple(() =>
		{
			Assert.That(result.Valid, Is.True);
			Assert.That(result.Error, Is.Null);
			Assert.That(result.Certificate, Is.Not.Null);
			Assert.That(result.Certificate!.Subject, Is.EqualTo(certificate.Subject));
			Assert.That(result.Certificate.Fingerprint,
				Is.EqualTo(certificate.GetCertHashString(HashAlgorithmName.SHA256)));
			Assert.That(result.Certificate.NotBefore, Is.EqualTo((DateTimeOffset)certificate.NotBefore));
			Assert.That(result.Certificate.NotAfter, Is.EqualTo((DateTimeOffset)certificate.NotAfter));
			Assert.That(result.Certificate.Source, Is.EqualTo(PublicTlsCertificateSource.Custom));
		});
	}

	[Test]
	public void A_Certificate_Paired_With_A_Different_Key_Is_Rejected()
	{
		var (certificatePem, _, _) = GenerateSelfSignedPem("CN=cert-owner.test");
		var (_, otherKeyPem, _) = GenerateSelfSignedPem("CN=someone-elses-key.test");

		var result = PublicTlsCertificateValidator.Validate(certificatePem,
			otherKeyPem,
			PublicTlsCertificateSource.Custom);

		Assert.Multiple(() =>
		{
			Assert.That(result.Valid, Is.False);
			Assert.That(result.Certificate, Is.Null);
			Assert.That(TestLocalization.Resolve(result.Error), Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void Missing_Certificate_And_Missing_Key_Report_Distinct_Messages()
	{
		var (_, privateKeyPem, _) = GenerateSelfSignedPem("CN=distinct-messages.test");

		var missingCertificate
			= PublicTlsCertificateValidator.Validate(" ", privateKeyPem, PublicTlsCertificateSource.Custom);
		var missingKey = PublicTlsCertificateValidator.Validate("not empty", "", PublicTlsCertificateSource.Custom);

		Assert.Multiple(() =>
		{
			Assert.That(missingCertificate.Valid, Is.False);
			Assert.That(missingKey.Valid, Is.False);
			Assert.That(TestLocalization.Resolve(missingCertificate.Error), Is.Not.EqualTo(TestLocalization.Resolve(missingKey.Error)));
		});
	}

	[Test]
	public void Garbage_Text_Is_Rejected_With_A_Message_Distinct_From_The_Missing_Field_Messages()
	{
		var missingCertificate
			= PublicTlsCertificateValidator.Validate(" ", "not empty", PublicTlsCertificateSource.Custom);
		var missingKey = PublicTlsCertificateValidator.Validate("not empty", " ", PublicTlsCertificateSource.Custom);
		var garbage = PublicTlsCertificateValidator.Validate("this is not pem",
			"neither is this",
			PublicTlsCertificateSource.Custom);

		Assert.Multiple(() =>
		{
			Assert.That(garbage.Valid, Is.False);
			Assert.That(TestLocalization.Resolve(garbage.Error), Is.Not.Null.And.Not.Empty);
			Assert.That(TestLocalization.Resolve(garbage.Error), Is.Not.EqualTo(TestLocalization.Resolve(missingCertificate.Error)));
			Assert.That(TestLocalization.Resolve(garbage.Error), Is.Not.EqualTo(TestLocalization.Resolve(missingKey.Error)));
		});
	}

	[Test]
	public void A_Passphrase_Encrypted_Private_Key_Is_Rejected_And_Names_The_Problem()
	{
		var (certificatePem, _, _) = GenerateSelfSignedPem("CN=encrypted-key.test");
		const string encryptedKeyPem =
			"-----BEGIN ENCRYPTED PRIVATE KEY-----\n" +
			"MIIFDDBABgkqhkiG9w0BBQ0wMzAbBgkqhkiG9w0BBQwwDgQIAAAAAAAAAAACAggA\n" +
			"-----END ENCRYPTED PRIVATE KEY-----\n";

		var result = PublicTlsCertificateValidator.Validate(certificatePem,
			encryptedKeyPem,
			PublicTlsCertificateSource.Custom);

		Assert.Multiple(() =>
		{
			Assert.That(result.Valid, Is.False);
			Assert.That(TestLocalization.Resolve(result.Error),
				Does.Contain("passphrase").IgnoreCase
					.Or.Contain("encrypted").IgnoreCase);
		});
	}

	[Test]
	public void No_Rejection_Message_Ever_Echoes_Back_Any_Part_Of_The_Input_Pem()
	{
		var (certificatePem, privateKeyPem, _) = GenerateSelfSignedPem("CN=confidentiality.test");
		var (_, mismatchedKeyPem, _) = GenerateSelfSignedPem("CN=confidentiality-other.test");
		const string encryptedKeyPem =
			"-----BEGIN ENCRYPTED PRIVATE KEY-----\n" +
			"MIIFDDBABgkqhkiG9w0BBQ0wMzAbBgkqhkiG9w0BBQwwDgQIAAAAAAAAAAACAggA\n" +
			"-----END ENCRYPTED PRIVATE KEY-----\n";

		PublicTlsValidationResult[] results =
		[
			PublicTlsCertificateValidator.Validate(certificatePem, mismatchedKeyPem, PublicTlsCertificateSource.Custom),
			PublicTlsCertificateValidator.Validate("garbage certificate text that is definitely not pem data at all",
				"garbage key text that is also definitely not pem data at all",
				PublicTlsCertificateSource.Custom),
			PublicTlsCertificateValidator.Validate(certificatePem, encryptedKeyPem, PublicTlsCertificateSource.Custom),
			PublicTlsCertificateValidator.Validate("", privateKeyPem, PublicTlsCertificateSource.Custom),
			PublicTlsCertificateValidator.Validate(certificatePem, "", PublicTlsCertificateSource.Custom)
		];

		Assert.Multiple(() =>
		{
			foreach (var result in results)
			{
				Assert.That(TestLocalization.Resolve(result.Error), Is.Not.Null);
				Assert.That(TestLocalization.Resolve(result.Error), Does.Not.Contain("BEGIN"));

				AssertNoLongSubstringLeaked(TestLocalization.Resolve(result.Error)!, certificatePem);
				AssertNoLongSubstringLeaked(TestLocalization.Resolve(result.Error)!, privateKeyPem);
				AssertNoLongSubstringLeaked(TestLocalization.Resolve(result.Error)!, mismatchedKeyPem);
				AssertNoLongSubstringLeaked(TestLocalization.Resolve(result.Error)!, encryptedKeyPem);
			}
		});
	}

	private static void AssertNoLongSubstringLeaked(string message, string pem)
	{
		const int windowLength = 40;
		for (var start = 0; start + windowLength <= pem.Length; start += windowLength)
		{
			var window = pem.Substring(start, windowLength);
			Assert.That(message,
				Does.Not.Contain(window),
				$"Message leaked a 40+ character fragment of input PEM: \"{window}\"");
		}
	}

	private static (string CertificatePem, string PrivateKeyPem, X509Certificate2 Certificate) GenerateSelfSignedPem(
		string subject)
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		var sanBuilder = new SubjectAlternativeNameBuilder();
		sanBuilder.AddDnsName("localhost");
		sanBuilder.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(sanBuilder.Build());

		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
			DateTimeOffset.UtcNow.AddYears(1));

		var certificatePem = certificate.ExportCertificatePem();
		var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();

		var reloaded = X509Certificate2.CreateFromPem(certificatePem);
		return (certificatePem, privateKeyPem, reloaded);
	}
}
