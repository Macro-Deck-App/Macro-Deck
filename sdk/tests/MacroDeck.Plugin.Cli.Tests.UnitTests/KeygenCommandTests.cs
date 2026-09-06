namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary><c>macrodeck-plugin keygen</c>: the happy path, the reserved-name refusal, and that it never
/// overwrites an existing key file.</summary>
[TestFixture]
public class KeygenCommandTests
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

	[Test]
	public async Task Keygen_writes_a_public_and_private_key_file_and_prints_the_public_key()
	{
		var (output, _, exitCode) = await CliRunner.Run("keygen",
			"--output",
			_directory,
			"--key-name",
			"my-creator",
			"--no-color");

		var publicPath = Path.Combine(_directory, "my-creator.public");
		var privatePath = Path.Combine(_directory, "my-creator.private");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(File.Exists(publicPath), Is.True);
			Assert.That(File.Exists(privatePath), Is.True);
			Assert.That(output, Does.Contain("Public key (base64):"));
		});

		var publicKeyText = (await File.ReadAllTextAsync(publicPath)).Trim();
		Assert.That(output, Does.Contain(publicKeyText));
	}

	[TestCase("root")]
	[TestCase("macrodeck-root")]
	[TestCase("registry")]
	[TestCase("macrodeck-registry")]
	public async Task A_reserved_key_name_is_rejected_and_writes_nothing(string reservedName)
	{
		var (_, error, exitCode) = await CliRunner.Run("keygen",
			"--output",
			_directory,
			"--key-name",
			reservedName,
			"--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Match(@"(?m)^error reserved-key-name: "));
			Assert.That(File.Exists(Path.Combine(_directory, $"{reservedName}.public")), Is.False);
			Assert.That(File.Exists(Path.Combine(_directory, $"{reservedName}.private")), Is.False);
		});
	}

	[Test]
	public async Task An_existing_public_key_file_is_not_overwritten_and_is_left_byte_identical()
	{
		var publicPath = Path.Combine(_directory, "my-creator.public");
		await File.WriteAllTextAsync(publicPath, "pre-existing content");

		var (_, error, exitCode) = await CliRunner.Run("keygen",
			"--output",
			_directory,
			"--key-name",
			"my-creator",
			"--no-color");

		var contentAfter = await File.ReadAllTextAsync(publicPath);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Match(@"(?m)^error output-exists: "));
			Assert.That(contentAfter, Is.EqualTo("pre-existing content"));
			Assert.That(File.Exists(Path.Combine(_directory, "my-creator.private")), Is.False);
		});
	}

	[Test]
	public async Task An_existing_private_key_file_is_not_overwritten_and_is_left_byte_identical()
	{
		var privatePath = Path.Combine(_directory, "my-creator.private");
		await File.WriteAllTextAsync(privatePath, "pre-existing content");

		var (_, error, exitCode) = await CliRunner.Run("keygen",
			"--output",
			_directory,
			"--key-name",
			"my-creator",
			"--no-color");

		var contentAfter = await File.ReadAllTextAsync(privatePath);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Match(@"(?m)^error output-exists: "));
			Assert.That(contentAfter, Is.EqualTo("pre-existing content"));
		});
	}
}
