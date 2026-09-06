using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PluginScaffoldValidationTests
{
	[TestCase("com.example.spotify")]
	[TestCase("com.example.spotify-controller")]
	public void ValidatePluginId_accepts_a_reverse_domain_id(string id)
	{
		Assert.That(PluginScaffoldValidation.ValidatePluginId(id), Is.Null);
	}

	[TestCase("Com.Example.Spotify")]
	[TestCase("spotify")]
	public void ValidatePluginId_rejects_an_invalid_id(string id)
	{
		var error = PluginScaffoldValidation.ValidatePluginId(id);

		Assert.That(error, Is.Not.Null);
		Assert.That(error!.Value.Code, Is.EqualTo("invalid-plugin-id"));
	}

	[TestCase("https://example.com")]
	[TestCase("http://example.com")]
	public void ValidateUrl_accepts_http_and_https(string url)
	{
		Assert.That(PluginScaffoldValidation.ValidateUrl(url, "repository"), Is.Null);
	}

	[TestCase("ftp://example.com")]
	[TestCase("git@github.com:example/spotify.git")]
	[TestCase("example.com")]
	[TestCase("")]
	public void ValidateUrl_rejects_anything_else(string url)
	{
		var error = PluginScaffoldValidation.ValidateUrl(url, "repository");

		Assert.That(error, Is.Not.Null);
		Assert.That(error!.Value.Code, Is.EqualTo("invalid-repository-url"));
	}

	[Test]
	public void ValidatePlatforms_accepts_the_known_vocabulary()
	{
		Assert.That(PluginScaffoldValidation.ValidatePlatforms(["win-x64", "osx-arm64"]), Is.Null);
	}

	[Test]
	public void ValidatePlatforms_rejects_an_unknown_rid()
	{
		var error = PluginScaffoldValidation.ValidatePlatforms(["win-x86"]);

		Assert.That(error, Is.Not.Null);
		Assert.That(error!.Value.Code, Is.EqualTo("unknown-platform"));
		Assert.That(error.Value.Message,
			Is.EqualTo("'win-x86' is not a supported target platform. Supported: win-x64, win-arm64, osx-arm64, " +
				"osx-x64, linux-x64, linux-arm64."));
	}

	[Test]
	public void ValidatePlatforms_rejects_an_empty_selection()
	{
		var error = PluginScaffoldValidation.ValidatePlatforms([]);

		Assert.That(error, Is.Not.Null);
		Assert.That(error!.Value.Code, Is.EqualTo("no-platform-selected"));
	}

	[Test]
	public void ValidateOutputDirectory_accepts_a_directory_that_does_not_exist()
	{
		var directory = Path.Combine(Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName, "child");

		Assert.That(PluginScaffoldValidation.ValidateOutputDirectory(directory), Is.Null);
	}

	[Test]
	public void ValidateOutputDirectory_accepts_an_existing_empty_directory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName;

		Assert.That(PluginScaffoldValidation.ValidateOutputDirectory(directory), Is.Null);
	}

	[Test]
	public void ValidateOutputDirectory_rejects_a_non_empty_directory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, "README.md"), "keep me");

		var error = PluginScaffoldValidation.ValidateOutputDirectory(directory);

		Assert.That(error, Is.Not.Null);
		Assert.That(error!.Value.Code, Is.EqualTo("output-exists"));
	}
}
