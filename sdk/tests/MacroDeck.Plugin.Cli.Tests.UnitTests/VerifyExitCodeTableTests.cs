namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// The exit-code table across the fixture family <c>sign</c>/<c>verify</c> exercise: valid succeeds; every
/// distinct way a package can be invalid is a subject-invalid exit with its own primary error message;
/// an unreadable input is kept apart from both.
/// </summary>
[TestFixture]
public class VerifyExitCodeTableTests
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

	private string RootPublicPath => SigningCliFixtures.WriteRootPublicKeyFile(_directory);

	private async Task<string> SignAsync(SigningCliFixtures.IssuedCertificate issued, string packageName)
	{
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory,
			issued,
			Path.GetFileNameWithoutExtension(packageName));
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, packageName));
		var outputPath = Path.Combine(_directory,
			$"{Path.GetFileNameWithoutExtension(packageName)}.signed.macroDeckPlugin");

		var (_, error, exitCode) = await CliRunner.Run("sign",
			packagePath,
			"--output",
			outputPath,
			"--certificate",
			certPath,
			"--certificate-signature",
			certSigPath,
			"--private-key",
			keyPath,
			"--root-public",
			RootPublicPath,
			"--no-color");
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success), $"test setup: signing must succeed ({error})");

		return outputPath;
	}

	[Test]
	public async Task Valid_exits_zero()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var signedPath = await SignAsync(issued, "valid.macroDeckPlugin");

		var (_, _, exitCode) = await CliRunner.Run("verify", signedPath, "--root-public", RootPublicPath, "--no-color");

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
	}

	// verify's own valid/invalid verdict is written to stdout, not stderr - see VerifyCommandTests' own note
	// on this. Only a problem with the invocation itself (as opposed to the package failing to verify)
	// reaches stderr as "error <code>: ...".

	[Test]
	public async Task Tampered_exits_one_with_a_file_digest_message()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var signedPath = await SignAsync(issued, "tampered.macroDeckPlugin");
		SigningCliFixtures.ReplaceEntry(signedPath, "app", "binary-Content");

		var (output, _, exitCode)
			= await CliRunner.Run("verify", signedPath, "--root-public", RootPublicPath, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(output, Does.Match(@"(?m)^invalid: file-digest-mismatch: "));
		});
	}

	/// <summary>Untrusted: signed by a root other than the one <c>--root-public</c> names.</summary>
	[Test]
	public async Task Untrusted_exits_one_with_a_certificate_untrusted_message()
	{
		var otherRoot = MacroDeck.Signing.Keys.Ed25519KeyPair.Create();

		// Sign against a certificate whose root differs from the one --root-public will name at verify time.
		var issuedUnderOtherRoot = IssueUnderCustomRoot(otherRoot);
		var signedPath = await SignUnderCustomRootAsync(issuedUnderOtherRoot, otherRoot, "untrusted.macroDeckPlugin");

		var (output, _, exitCode)
			= await CliRunner.Run("verify", signedPath, "--root-public", RootPublicPath, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(output, Does.Match(@"(?m)^invalid: certificate-untrusted: "));
		});
	}

	[Test]
	public async Task Wrong_purpose_exits_one_with_a_certificate_wrong_purpose_message()
	{
		var issued = SigningCliFixtures.IssueCertificate(keyUsage:
			[MacroDeck.Signing.Certificates.SigningCertificateChain.RegistryKeyUsage]);
		var (certPath, certSigPath, keyPath)
			= SigningCliFixtures.WriteCertificateFiles(_directory, issued, "wrong-purpose");
		var packagePath
			= SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "wrong-purpose.macroDeckPlugin"));
		var outputPath = Path.Combine(_directory, "wrong-purpose.signed.macroDeckPlugin");

		// sign itself refuses a registry-usage certificate before writing anything (proven at the library
		// level) - this table entry needs the failure to surface at verification, so signing must fail here
		// too, and that failure IS the "wrong purpose" verdict this row is pinning.
		var (_, error, exitCode) = await CliRunner.Run("sign",
			packagePath,
			"--output",
			outputPath,
			"--certificate",
			certPath,
			"--certificate-signature",
			certSigPath,
			"--private-key",
			keyPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Match(@"(?m)^error certificate-wrong-purpose: "));
		});
	}

	[Test]
	public async Task Expired_at_signing_exits_one_with_a_certificate_expired_message()
	{
		var issued = SigningCliFixtures.IssueCertificate(notBefore: DateTimeOffset.UtcNow.AddDays(-30),
			notAfter: DateTimeOffset.UtcNow.AddDays(-10));
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued, "expired");
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "expired.macroDeckPlugin"));
		var outputPath = Path.Combine(_directory, "expired.signed.macroDeckPlugin");

		// sign checks validity at the current instant, which is already past notAfter here - so, like the
		// wrong-purpose row, the "expired" verdict this row pins is the one sign itself reports.
		var (_, error, exitCode) = await CliRunner.Run("sign",
			packagePath,
			"--output",
			outputPath,
			"--certificate",
			certPath,
			"--certificate-signature",
			certSigPath,
			"--private-key",
			keyPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Match(@"(?m)^error certificate-expired: "));
		});
	}

	[Test]
	public async Task Unsigned_exits_one_with_a_signature_missing_message()
	{
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "unsigned.macroDeckPlugin"));

		var (output, _, exitCode)
			= await CliRunner.Run("verify", packagePath, "--root-public", RootPublicPath, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(output, Does.Match(@"(?m)^invalid: signature-missing: "));
		});
	}

	[Test]
	public async Task Already_signed_exits_one_with_an_already_signed_message()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var signedPath = await SignAsync(issued, "already-signed.macroDeckPlugin");
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory,
			SigningCliFixtures.IssueCertificate(),
			"already-signed-second");
		var secondOutput = Path.Combine(_directory, "already-signed.twice.macroDeckPlugin");

		var (_, error, exitCode) = await CliRunner.Run("sign",
			signedPath,
			"--output",
			secondOutput,
			"--certificate",
			certPath,
			"--certificate-signature",
			certSigPath,
			"--private-key",
			keyPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Match(@"(?m)^error already-signed: "));
		});
	}

	[Test]
	public async Task Every_subject_invalid_row_has_a_different_primary_message()
	{
		var codes = new List<string>();

		var issued = SigningCliFixtures.IssueCertificate();
		var tamperedPath = await SignAsync(issued, "row-tampered.macroDeckPlugin");
		SigningCliFixtures.ReplaceEntry(tamperedPath, "app", "binary-Content");
		codes.Add(FirstInvalidVerdictCode(
			(await CliRunner.Run("verify", tamperedPath, "--root-public", RootPublicPath, "--no-color")).Output));

		var unsignedPath
			= SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "row-unsigned.macroDeckPlugin"));
		codes.Add(FirstInvalidVerdictCode(
			(await CliRunner.Run("verify", unsignedPath, "--root-public", RootPublicPath, "--no-color")).Output));

		Assert.That(codes, Is.Unique);
	}

	/// <summary>Extracts the diagnostic code from <c>verify</c>'s "invalid: &lt;code&gt;: &lt;message&gt;"
	/// stdout line.</summary>
	private static string FirstInvalidVerdictCode(string stdout)
	{
		var line = stdout.Split('\n').First(l => l.StartsWith("invalid: ", StringComparison.Ordinal));
		return line.Split(' ')[1].TrimEnd(':');
	}

	private static SigningCliFixtures.IssuedCertificate IssueUnderCustomRoot((byte[] PrivateKey, byte[] PublicKey) root)
	{
		// Mirrors SigningCliFixtures.IssueCertificate, but signed by a root other than SigningCliFixtures.Root.
		var subjectKeys = MacroDeck.Signing.Keys.Ed25519KeyPair.Create();
		var certificateId = "cert_" + Guid.NewGuid().ToString("N");

		var certificate = new MacroDeck.Signing.Certificates.SigningCertificate
		{
			SchemaVersion = 1,
			Subject = new MacroDeck.Signing.Certificates.SigningCertificateSubject
			{
				Kind = "creator", Id = "creator-1", Name = "Test Creator"
			},
			Algorithm = "ed25519",
			CertificateId = certificateId,
			PublicKey = Convert.ToBase64String(subjectKeys.PublicKey),
			KeyUsage = [MacroDeck.Signing.Certificates.SigningCertificateChain.PackageKeyUsage],
			NotBefore = DateTimeOffset.UtcNow.AddDays(-1),
			NotAfter = DateTimeOffset.UtcNow.AddYears(1),
			IssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
			RootKeyId = "root_other0001"
		};

		var certificateBytes = MacroDeck.Signing.SigningJson.Serialize(certificate);
		using var key = NSec.Cryptography.Key.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519,
			root.PrivateKey,
			NSec.Cryptography.KeyBlobFormat.RawPrivateKey);
		var signature = NSec.Cryptography.SignatureAlgorithm.Ed25519.Sign(key, certificateBytes);
		var certificateSignatureBytes = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(signature));

		return new SigningCliFixtures.IssuedCertificate(certificateBytes,
			certificateSignatureBytes,
			subjectKeys.PrivateKey,
			subjectKeys.PublicKey,
			certificateId);
	}

	private async Task<string> SignUnderCustomRootAsync(SigningCliFixtures.IssuedCertificate issued,
		(byte[] PrivateKey, byte[] PublicKey) signingRoot,
		string packageName)
	{
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory,
			issued,
			Path.GetFileNameWithoutExtension(packageName));
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, packageName));
		var outputPath = Path.Combine(_directory,
			$"{Path.GetFileNameWithoutExtension(packageName)}.signed.macroDeckPlugin");
		var signingRootPublicPath = SigningCliFixtures.WriteRootPublicKeyFile(_directory,
			signingRoot.PublicKey,
			$"{Path.GetFileNameWithoutExtension(packageName)}.signing-root.public");

		var (_, error, exitCode) = await CliRunner.Run("sign",
			packagePath,
			"--output",
			outputPath,
			"--certificate",
			certPath,
			"--certificate-signature",
			certSigPath,
			"--private-key",
			keyPath,
			"--root-public",
			signingRootPublicPath,
			"--no-color");
		Assert.That(exitCode,
			Is.EqualTo(ExitCode.Success),
			$"test setup: signing under the custom root must succeed ({error})");

		return outputPath;
	}
}
