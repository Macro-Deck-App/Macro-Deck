using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>macrodeck-plugin verify</c>. Every scenario here signs with <see cref="SigningCliFixtures.Root" /> and
/// verifies with <c>--root-public</c> pointed at that same root's public key file - the real Macro Deck
/// root's private key exists only in the offline key-generation tool outside this repository, so a test can
/// never anchor to it. That also means every one of these runs sees the <c>non-production-root</c> warning
/// and <c>rootAnchored: false</c>; the one test that asserts on that fact directly is not special-cased,
/// it is simply what every run here looks like.
/// </summary>
[TestFixture]
public class VerifyCommandTests
{
	/// <summary><c>verify</c>'s own valid/invalid verdict is written to stdout via <c>console.WriteLine</c>,
	/// not <c>console.WriteError</c> - "error &lt;code&gt;: ..." on stderr is reserved for a problem with
	/// the invocation itself (an unreadable <c>--root-public</c> file, an internal error), never for the
	/// package failing to verify. A failed verify reads "invalid: &lt;code&gt;: &lt;message&gt;" on stdout.
	/// </summary>
	private static readonly Regex _invalidVerdictShape =
		new(@"^invalid: [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.Multiline);

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

	private async Task<string> SignValidPackageAsync(SigningCliFixtures.IssuedCertificate? issuedOverride = null)
	{
		var issued = issuedOverride ?? SigningCliFixtures.IssueCertificate();
		var (certPath, certSigPath, keyPath) = SigningCliFixtures.WriteCertificateFiles(_directory, issued);
		var packagePath = SigningCliFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"));
		var outputPath = Path.Combine(_directory, "plugin.signed.macroDeckPlugin");

		var (_, _, exitCode) = await CliRunner.Run("sign",
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
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success), "test setup: signing must succeed");

		return outputPath;
	}

	[Test]
	public async Task A_validly_signed_package_verifies_with_exit_zero()
	{
		var signedPath = await SignValidPackageAsync();

		var (output, _, exitCode) = await CliRunner.Run("verify",
			signedPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(output, Does.Contain("valid"));
		});
	}

	[Test]
	public async Task Verify_always_prints_a_revocation_notice_on_success()
	{
		var signedPath = await SignValidPackageAsync();

		var (output, error, _) = await CliRunner.Run("verify",
			signedPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.That(output + error, Does.Contain("Revocation was not checked"));
	}

	[Test]
	public async Task Verify_always_prints_a_revocation_notice_on_failure_too()
	{
		var signedPath = await SignValidPackageAsync();
		SigningCliFixtures.ReplaceEntry(signedPath, "app", "binary-Content");

		var (output, error, exitCode) = await CliRunner.Run("verify",
			signedPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(output + error, Does.Contain("Revocation was not checked"));
		});
	}

	[Test]
	public async Task
		Verify_output_json_writes_parseable_JSON_to_stdout_with_diagnostics_on_stderr_and_revocationChecked_false()
	{
		var signedPath = await SignValidPackageAsync();

		var (output, error, exitCode) = await CliRunner.Run("verify",
			signedPath,
			"--root-public",
			RootPublicPath,
			"--output",
			"json",
			"--no-color");

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var document = JsonDocument.Parse(output);
		var root = document.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(root.GetProperty("valid").GetBoolean(), Is.True);
			Assert.That(root.GetProperty("revocationChecked").GetBoolean(), Is.False);
			Assert.That(root.GetProperty("rootAnchored").GetBoolean(), Is.False);
			// Diagnostics (the non-production-root warning) land on stderr, keeping stdout machine-parseable.
			Assert.That(error, Does.Contain("non-production-root"));
		});
	}

	[Test]
	public async Task Verify_output_json_on_failure_also_reports_revocationChecked_false()
	{
		var signedPath = await SignValidPackageAsync();
		SigningCliFixtures.ReplaceEntry(signedPath, "app", "binary-Content");

		var (output, _, exitCode) = await CliRunner.Run("verify",
			signedPath,
			"--root-public",
			RootPublicPath,
			"--output",
			"json",
			"--no-color");

		Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));

		using var document = JsonDocument.Parse(output);
		var root = document.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(root.GetProperty("valid").GetBoolean(), Is.False);
			Assert.That(root.GetProperty("revocationChecked").GetBoolean(), Is.False);
		});
	}

	[Test]
	public async Task A_non_pinned_root_public_prints_the_warning_and_reports_rootAnchored_false()
	{
		var signedPath = await SignValidPackageAsync();

		var (_, error, exitCode) = await CliRunner.Run("verify",
			signedPath,
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
	public async Task Verify_of_a_missing_file_exits_input_unreadable()
	{
		var missingPath = Path.Combine(_directory, "does-not-exist.macroDeckPlugin");

		var (output, error, exitCode) = await CliRunner.Run("verify",
			missingPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(output, Does.Match(_invalidVerdictShape));
			Assert.That(output + error, Does.Not.Contain("Unhandled exception"));
			Assert.That(output + error, Does.Not.Contain("   at "));
		});
	}

	[Test]
	public async Task Verify_of_a_non_zip_file_exits_input_unreadable()
	{
		var notAZipPath = Path.Combine(_directory, "notazip.macroDeckPlugin");
		await File.WriteAllTextAsync(notAZipPath, "this is not a zip file");

		var (output, _, exitCode) = await CliRunner.Run("verify",
			notAZipPath,
			"--root-public",
			RootPublicPath,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(output, Does.Match(_invalidVerdictShape));
		});
	}

	[Test]
	public async Task Verify_with_an_unknown_flag_is_a_usage_error()
	{
		var signedPath = await SignValidPackageAsync();

		var (_, error, exitCode)
			= await CliRunner.Run("verify", signedPath, "--not-a-real-flag", "value", "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Is.Not.Empty);
		});
	}

	[Test]
	public async Task Verify_with_no_package_argument_is_a_usage_error()
	{
		var (_, error, exitCode) = await CliRunner.Run("verify", "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Is.Not.Empty);
		});
	}

	[Test]
	public async Task No_verify_invocation_in_this_fixture_ever_prints_a_stack_trace_or_exits_seventy()
	{
		var signedPath = await SignValidPackageAsync();
		var missingPath = Path.Combine(_directory, "missing.macroDeckPlugin");

		var results = new[]
		{
			await CliRunner.Run("verify", signedPath, "--root-public", RootPublicPath, "--no-color"),
			await CliRunner.Run("verify", missingPath, "--root-public", RootPublicPath, "--no-color"),
			await CliRunner.Run("verify", "--no-color"),
			await CliRunner.Run("verify", signedPath, "--bogus-flag", "--no-color")
		};

		Assert.Multiple(() =>
		{
			foreach (var (output, error, exitCode) in results)
			{
				Assert.That(exitCode, Is.Not.EqualTo(70));
				Assert.That(output + error, Does.Not.Contain("Unhandled exception"));
				Assert.That(output + error, Does.Not.Contain("   at "));
			}
		});
	}
}
