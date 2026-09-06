using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Infrastructure.Network.Tls;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Network.Tls;

[TestFixture]
public class FilePublicTlsCertificateStoreTests
{
	private TestPaths _paths = null!;
	private IDataProtectionProvider _dataProtectionProvider = null!;
	private FilePublicTlsCertificateStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.KeysDirectory);
		_dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(_paths.KeysDirectory));
		_store = new FilePublicTlsCertificateStore(_dataProtectionProvider,
			_paths,
			new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Absent_Files_Report_NotConfigured()
	{
		var resolution = _store.LoadServerCertificate();

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Failure, Is.EqualTo(PublicTlsFailure.NotConfigured));
			Assert.That(resolution.Certificate, Is.Null);
		});
	}

	[Test]
	public void ReadInfo_Returns_Null_When_Nothing_Is_Stored()
	{
		Assert.That(_store.ReadInfo(), Is.Null);
	}

	[Test]
	public void A_Saved_Pair_Round_Trips_Into_A_Usable_Server_Certificate()
	{
		var (certificatePem, privateKeyPem) = GenerateSelfSignedPem();

		var saved = _store.Save(certificatePem, privateKeyPem, PublicTlsCertificateSource.SelfSigned);
		var resolution = _store.LoadServerCertificate();

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Failure, Is.EqualTo(PublicTlsFailure.None));
			Assert.That(resolution.Certificate, Is.Not.Null);
			Assert.That(resolution.Certificate!.HasPrivateKey, Is.True);
			Assert.That(resolution.Certificate.GetCertHashString(HashAlgorithmName.SHA256),
				Is.EqualTo(saved.Fingerprint));
			using var rsa = resolution.Certificate.GetRSAPrivateKey();
			Assert.That(rsa, Is.Not.Null);
		});

		resolution.Certificate?.Dispose();
	}

	[Test]
	public void ReadInfo_Reports_Metadata_Matching_What_Save_Returned()
	{
		var (certificatePem, privateKeyPem) = GenerateSelfSignedPem();

		var saved = _store.Save(certificatePem, privateKeyPem, PublicTlsCertificateSource.Custom);
		var info = _store.ReadInfo();

		Assert.Multiple(() =>
		{
			Assert.That(info, Is.Not.Null);
			Assert.That(info!.Fingerprint, Is.EqualTo(saved.Fingerprint));
			Assert.That(info.Subject, Is.EqualTo(saved.Subject));
			Assert.That(info.Source, Is.EqualTo(PublicTlsCertificateSource.Custom));
		});
	}

	[Test]
	public void The_Stored_Authority_Key_File_Is_Not_Pem_In_The_Clear()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", DateTimeOffset.UtcNow);
		_store.SaveAuthority(authority.CertificatePem, authority.PrivateKeyPem);

		var keyFileText = Encoding.UTF8.GetString(
			File.ReadAllBytes(Path.Combine(_paths.KeysDirectory, "public-tls.ca.key")));

		Assert.Multiple(() =>
		{
			Assert.That(keyFileText, Does.Not.Contain("BEGIN PRIVATE KEY"));
			Assert.That(keyFileText, Does.Not.Contain(authority.PrivateKeyPem));
		});
	}

	// The certificate a device has to install is public material, so it stays available even when the
	// key ring cannot open the authority's private half.
	[Test]
	public void The_Authority_Certificate_Is_Readable_Without_Unprotecting_Anything()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", DateTimeOffset.UtcNow);
		_store.SaveAuthority(authority.CertificatePem, authority.PrivateKeyPem);

		File.WriteAllBytes(Path.Combine(_paths.KeysDirectory, "public-tls.ca.key"), [1, 2, 3, 4]);

		Assert.Multiple(() =>
		{
			Assert.That(_store.ReadAuthorityCertificatePem(), Is.EqualTo(authority.CertificatePem));
			Assert.That(_store.ReadAuthorityInfo(), Is.Not.Null);
			Assert.That(_store.LoadAuthority().Failure, Is.EqualTo(PublicTlsFailure.KeyUnreadable));
		});
	}

	[Test]
	public void A_Saved_Authority_Round_Trips_Into_Signing_Material()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", DateTimeOffset.UtcNow);
		_store.SaveAuthority(authority.CertificatePem, authority.PrivateKeyPem);

		var loaded = _store.LoadAuthority();
		var host = LocalCertificateAuthority.IssueHostCertificate(loaded.Material!.Value,
			[IPAddress.Parse("192.168.1.42")],
			[],
			DateTimeOffset.UtcNow);

		using var authorityCertificate = X509Certificate2.CreateFromPem(authority.CertificatePem);
		using var hostCertificate = X509Certificate2.CreateFromPem(host.CertificatePem);

		Assert.That(LocalCertificateAuthority.IsIssuedBy(hostCertificate, authorityCertificate), Is.True);
	}

	[Test]
	public void The_Stored_Key_File_Is_Not_Pem_In_The_Clear()
	{
		var (certificatePem, privateKeyPem) = GenerateSelfSignedPem();
		_store.Save(certificatePem, privateKeyPem, PublicTlsCertificateSource.SelfSigned);

		var keyFilePath = Path.Combine(_paths.KeysDirectory, "public-tls.key");
		var keyFileBytes = File.ReadAllBytes(keyFilePath);
		var keyFileText = Encoding.UTF8.GetString(keyFileBytes);

		Assert.Multiple(() =>
		{
			Assert.That(keyFileText, Does.Not.Contain("BEGIN PRIVATE KEY"));
			Assert.That(keyFileText, Does.Not.Contain(privateKeyPem));
		});
	}

	[Test]
	public void A_Key_File_That_Cannot_Be_Unprotected_Reports_KeyUnreadable_And_Leaves_Files_Untouched()
	{
		var (certificatePem, privateKeyPem) = GenerateSelfSignedPem();
		_store.Save(certificatePem, privateKeyPem, PublicTlsCertificateSource.SelfSigned);

		var keyFilePath = Path.Combine(_paths.KeysDirectory, "public-tls.key");
		var certificateFilePath = Path.Combine(_paths.KeysDirectory, "public-tls.crt.pem");
		var junk = "not a protected payload"u8.ToArray();
		File.WriteAllBytes(keyFilePath, junk);
		var certificateBytesBeforeLoad = File.ReadAllBytes(certificateFilePath);

		var resolution = _store.LoadServerCertificate();

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Failure, Is.EqualTo(PublicTlsFailure.KeyUnreadable));
			Assert.That(resolution.Certificate, Is.Null);
			Assert.That(File.ReadAllBytes(keyFilePath), Is.EqualTo(junk));
			Assert.That(File.ReadAllBytes(certificateFilePath), Is.EqualTo(certificateBytesBeforeLoad));
		});
	}

	[Test]
	public void Saving_A_Second_Certificate_Replaces_Both_Files_Together()
	{
		var (firstCertificatePem, firstPrivateKeyPem) = GenerateSelfSignedPem();
		var firstSaved = _store.Save(firstCertificatePem, firstPrivateKeyPem, PublicTlsCertificateSource.SelfSigned);

		var (secondCertificatePem, secondPrivateKeyPem) = GenerateSelfSignedPem();
		var secondSaved = _store.Save(secondCertificatePem, secondPrivateKeyPem, PublicTlsCertificateSource.Custom);

		var resolution = _store.LoadServerCertificate();

		Assert.Multiple(() =>
		{
			Assert.That(firstSaved.Fingerprint, Is.Not.EqualTo(secondSaved.Fingerprint));
			Assert.That(resolution.Failure, Is.EqualTo(PublicTlsFailure.None));
			Assert.That(resolution.Certificate!.GetCertHashString(HashAlgorithmName.SHA256),
				Is.EqualTo(secondSaved.Fingerprint));
			Assert.That(_store.ReadInfo()!.Source, Is.EqualTo(PublicTlsCertificateSource.Custom));
		});

		resolution.Certificate?.Dispose();
	}

	private static (string CertificatePem, string PrivateKeyPem) GenerateSelfSignedPem()
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=store-test.macrodeck",
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		var sanBuilder = new SubjectAlternativeNameBuilder();
		sanBuilder.AddDnsName("localhost");
		sanBuilder.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(sanBuilder.Build());

		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
			DateTimeOffset.UtcNow.AddYears(1));

		return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
	}
}
