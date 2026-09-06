using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>macrodeck-plugin sign</c>. As in <see cref="VerifyCommandTests" />, every scenario signs against
/// <see cref="SigningCliFixtures.Root" /> and passes <c>--root-public</c> pointed at it, since the real
/// Macro Deck root's private key exists only outside this repository.
/// </summary>
[TestFixture]
public class SignCommandTests
{
	private static readonly Regex _errorShape = new(@"^error [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.Multiline);

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

	[Test]
	public async Task Signing_a_valid_package_succeeds_and_the_output_verifies()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued);
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));
		var outputPath = Path.Combine(_directory, "plugin.signed.macroDeckPlugin");

		var (output, _, exitCode) = await CliRunner.Run("sign",
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
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(File.Exists(outputPath), Is.True);
			Assert.That(output, Does.Contain(issued.CertificateId));
		});

		var (_, _, verifyExitCode) = await CliRunner.Run("verify",
			outputPath,
			"--root-public",
			RootPublicPath,
			"--no-color");
		Assert.That(verifyExitCode, Is.EqualTo(ExitCode.Success));
	}

	[Test]
	public async Task Signing_an_already_signed_package_is_refused_with_a_distinct_message()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued);
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));
		var firstOutput = Path.Combine(_directory, "plugin.signed-once.macroDeckPlugin");
		var secondOutput = Path.Combine(_directory, "plugin.signed-twice.macroDeckPlugin");

		await CliRunner.Run("sign",
			packagePath,
			"--output",
			firstOutput,
			"--certificate",
			certPath,
			"--certificate-signature",
			certSigPath,
			"--private-key",
			keyPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		var (_, error, exitCode) = await CliRunner.Run("sign",
			firstOutput,
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
			Assert.That(File.Exists(secondOutput), Is.False);
		});
	}

	[Test]
	public async Task A_non_pinned_root_public_prints_the_warning_when_signing_too()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued);
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));
		var outputPath = Path.Combine(_directory, "plugin.signed.macroDeckPlugin");

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
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(error, Does.Match(@"(?m)^warning non-production-root: "));
		});
	}

	[Test]
	public async Task Signing_a_missing_file_exits_input_unreadable()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued);
		var missingPath = Path.Combine(_directory, "does-not-exist.macroDeckPlugin");
		var outputPath = Path.Combine(_directory, "plugin.signed.macroDeckPlugin");

		var (_, error, exitCode) = await CliRunner.Run("sign",
			missingPath,
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
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(error, Does.Match(_errorShape));
		});
	}

	[Test]
	public async Task Signing_a_non_zip_file_exits_input_unreadable()
	{
		var issued = SigningCliFixtures.IssueCertificate();
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued);
		var notAZipPath = Path.Combine(_directory, "notazip.macroDeckPlugin");
		await File.WriteAllTextAsync(notAZipPath, "not a zip file");
		var outputPath = Path.Combine(_directory, "plugin.signed.macroDeckPlugin");

		var (_, error, exitCode) = await CliRunner.Run("sign",
			notAZipPath,
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
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(error, Does.Match(_errorShape));
		});
	}

	[Test]
	public async Task Signing_with_an_unknown_flag_is_a_usage_error()
	{
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));

		var (_, error, exitCode) = await CliRunner.Run("sign", packagePath, "--not-a-real-flag", "value", "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Is.Not.Empty);
		});
	}

	[Test]
	public async Task Signing_with_a_missing_required_option_is_a_usage_error()
	{
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));

		// --certificate, --certificate-signature and --private-key are all required and none are supplied.
		var (_, error, exitCode) = await CliRunner.Run("sign",
			packagePath,
			"--output",
			Path.Combine(_directory, "out.macroDeckPlugin"),
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Is.Not.Empty);
		});
	}
}
