using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class IssuerSigningCliTests
{
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	private string RootPublicPath => SigningCliFixtures.WriteRootPublicKeyFile(_directory, TestPki.Root.PublicKey);

	private string Write(string name, byte[] content)
	{
		var path = Path.Combine(_directory, name);
		File.WriteAllBytes(path, content);
		return path;
	}

	private string WriteKey(byte[] privateKey)
	{
		var path = Path.Combine(_directory, "creator.private");
		File.WriteAllText(path, Convert.ToBase64String(privateKey) + "\n");
		return path;
	}

	private async Task<(string Output, string Error, int ExitCode, string SignedPath)> Sign(
		TestPki.IssuedCertificate certificate,
		params string[] extraArguments)
	{
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));
		var signedPath = Path.Combine(_directory, "plugin.signed.macroDeckPlugin");
		string[] arguments =
		[
			"sign",
			packagePath,
			"--output",
			signedPath,
			"--certificate",
			Write("certificate.json", certificate.CertificateBytes),
			"--certificate-signature",
			Write("certificate.sig", certificate.CertificateSignatureBytes),
			"--private-key",
			WriteKey(certificate.PrivateKey),
			"--root-public",
			RootPublicPath,
			"--no-color",
			.. extraArguments
		];

		var (output, error, exitCode) = await CliRunner.Run(arguments);
		return (output, error, exitCode, signedPath);
	}

	private string[] IssuerArguments(TestPki.IssuedCertificate issuer) =>
	[
		"--issuer-certificate",
		Write("issuer.json", issuer.CertificateBytes),
		"--issuer-certificate-signature",
		Write("issuer.sig", issuer.CertificateSignatureBytes)
	];

	[Test]
	public async Task A_certificate_from_an_issuer_signs_and_verifies_naming_the_issuer()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var (_, signError, signExit, signedPath) = await Sign(certificate, IssuerArguments(issuer));
		var (output, _, verifyExit) = await CliRunner.Run("verify",
			signedPath,
			"--root-public",
			RootPublicPath,
			"--output",
			"json",
			"--no-color");

		using var json = JsonDocument.Parse(output);
		Assert.Multiple(() =>
		{
			Assert.That(signExit, Is.EqualTo(ExitCode.Success), signError);
			Assert.That(verifyExit, Is.EqualTo(ExitCode.Success), output);
			Assert.That(json.RootElement.GetProperty("valid").GetBoolean(), Is.True);
			Assert.That(json.RootElement.GetProperty("issuerCertificateId").GetString(),
				Is.EqualTo(issuer.CertificateId));
		});
	}

	[Test]
	public async Task Verify_reports_a_missing_issuer_as_its_own_code()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var (_, signError, signExit, signedPath) = await Sign(certificate, IssuerArguments(issuer));
		PackageArchiveFixtures.RemoveEntry(signedPath, PluginArtifactFiles.IssuerCertificateFileName);
		PackageArchiveFixtures.RemoveEntry(signedPath, PluginArtifactFiles.IssuerCertificateSignatureFileName);

		var (output, _, exitCode) = await CliRunner.Run("verify", signedPath, "--root-public", RootPublicPath, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(signExit, Is.EqualTo(ExitCode.Success), signError);
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(output, Does.Contain("certificate-issuer-missing"));
		});
	}

	[Test]
	public async Task Signing_with_an_issuer_signed_certificate_but_no_issuer_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var (_, error, exitCode, signedPath) = await Sign(certificate);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("certificate-issuer-missing"));
			Assert.That(File.Exists(signedPath), Is.False);
		});
	}

	[Test]
	public async Task Signing_with_only_one_issuer_option_is_a_usage_error()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var (_, _, exitCode, _) = await Sign(certificate,
			"--issuer-certificate",
			Write("issuer.json", issuer.CertificateBytes));

		Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
	}

	[Test]
	public async Task Signing_with_an_unreadable_issuer_certificate_is_an_input_error()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var (_, error, exitCode, _) = await Sign(certificate,
			"--issuer-certificate",
			Path.Combine(_directory, "missing.json"),
			"--issuer-certificate-signature",
			Write("issuer.sig", issuer.CertificateSignatureBytes));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(error, Does.Contain("certificate-unreadable"));
		});
	}

	[Test]
	public async Task Validate_accepts_the_issuer_files_of_a_signed_artifact()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var (_, signError, signExit, signedPath) = await Sign(certificate, IssuerArguments(issuer));

		var result = await ManifestValidator.ValidateArtifactAsync(signedPath);

		Assert.Multiple(() =>
		{
			Assert.That(signExit, Is.EqualTo(ExitCode.Success), signError);
			Assert.That(result.Problems.Select(problem => problem.Code), Does.Not.Contain("undeclared-file"));
		});
	}

	[Test]
	public async Task Validate_flags_issuer_files_in_an_unsigned_artifact()
	{
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));
		PackageArchiveFixtures.AddEntry(packagePath, PluginArtifactFiles.IssuerCertificateFileName, "{}");

		var result = await ManifestValidator.ValidateArtifactAsync(packagePath);

		Assert.That(result.Problems.Select(problem => problem.Code), Does.Contain("undeclared-file"));
	}

	[Test]
	public async Task Validate_flags_issuer_files_in_an_artifact_signed_directly_under_the_root()
	{
		var (_, signError, signExit, signedPath) = await Sign(TestPki.IssueCertificate());
		PackageArchiveFixtures.AddEntry(signedPath, PluginArtifactFiles.IssuerCertificateFileName, "{}");

		var result = await ManifestValidator.ValidateArtifactAsync(signedPath);

		Assert.Multiple(() =>
		{
			Assert.That(signExit, Is.EqualTo(ExitCode.Success), signError);
			Assert.That(result.Problems.Select(problem => problem.Code), Does.Contain("undeclared-file"));
		});
	}
}
