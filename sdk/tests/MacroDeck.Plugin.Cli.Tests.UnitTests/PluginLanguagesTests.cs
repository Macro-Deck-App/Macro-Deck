using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// Deriving a manifest's <c>languages</c> from a project's resource files, and what <c>pack</c> does with
/// the result. The expectation throughout comes from the localization contract - an unsuffixed
/// <c>Strings.resx</c> is English, a suffix is a BCP-47 tag carried whole - not from re-running the
/// generator and comparing against whatever it produced.
/// </summary>
[TestFixture]
public class PluginLanguagesTests
{
	private static readonly string[] _expectedFromFixture = ["de", "en", "zh-Hant-TW"];

	private static readonly string[] _authoredLanguages = ["en", "fr"];

	private static string WriteResources(string directory, params string[] fileNames)
	{
		var resourceDirectory = Path.Combine(directory, PluginLanguages.ResourceDirectoryName);
		Directory.CreateDirectory(resourceDirectory);

		foreach (var fileName in fileNames)
		{
			File.WriteAllText(Path.Combine(resourceDirectory, fileName),
				"""<?xml version="1.0" encoding="utf-8"?><root />""");
		}

		return resourceDirectory;
	}

	[Test]
	public void A_resource_set_yields_the_default_language_and_every_culture_suffix()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;

		try
		{
			WriteResources(directory,
				"Strings.resx",
				"Strings.de.resx",
				"Strings.zh-Hant-TW.resx",
				// MDLOC005 territory: the generator refuses to compile this suffix, so it must never
				// reach a manifest either.
				"Strings.not-a-culture-at-all.resx");

			Assert.That(PluginLanguages.Discover(directory), Is.EqualTo(_expectedFromFixture));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void A_project_with_no_resources_declares_no_languages()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;

		try
		{
			Assert.That(PluginLanguages.Discover(directory), Is.Empty);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task Packing_a_project_tree_writes_its_resource_languages_into_the_artifact()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			WriteResources(directory, "Strings.resx", "Strings.de.resx", "Strings.zh-Hant-TW.resx");

			var result = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, PluginArtifactFiles.ManifestFileName),
				_ => outputPath,
				force: false);

			Assert.That(result.Success, Is.True, result.FailureMessage);

			// Read back through the reader the host itself uses, not through the packer's own result.
			var reader = new PluginArtifactReader(new PluginManifestReader(), Serilog.Core.Logger.None);
			var inspection = await reader.Inspect(outputPath);

			Assert.That(inspection.Success, Is.True, inspection.ErrorMessage);
			Assert.That(inspection.Manifest!.Languages, Is.EqualTo(_expectedFromFixture));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	/// <summary>The staged/published case: nothing to discover, so the declared value is the only thing
	/// that still knows which languages went into the build and must survive the pack.</summary>
	[Test]
	public async Task Packing_a_payload_with_no_resources_carries_the_declared_languages_through()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var manifestPath = Path.Combine(directory, PluginArtifactFiles.ManifestFileName);
			await File.WriteAllTextAsync(manifestPath,
				ManifestFixtures.ValidManifestJson()
					.Replace("\"manifestVersion\": 1,",
						"\"manifestVersion\": 1,\n\t\"languages\": [\"en\", \"fr\"],",
						StringComparison.Ordinal));

			var result = await PluginPacker.PackAsync(directory, manifestPath, _ => outputPath, force: false);

			Assert.That(result.Success, Is.True, result.FailureMessage);

			var reader = new PluginArtifactReader(new PluginManifestReader(), Serilog.Core.Logger.None);
			var inspection = await reader.Inspect(outputPath);

			Assert.Multiple(() =>
			{
				Assert.That(inspection.Success, Is.True, inspection.ErrorMessage);
				Assert.That(inspection.Manifest!.Languages, Is.EqualTo(_authoredLanguages));
				Assert.That(result.Warnings.Select(warning => warning.Code),
					Does.Not.Contain("languages-recomputed"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	[Test]
	public async Task Replacing_a_declared_language_list_is_reported_rather_than_done_silently()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			WriteResources(directory, "Strings.resx", "Strings.de.resx");

			var manifestPath = Path.Combine(directory, PluginArtifactFiles.ManifestFileName);
			await File.WriteAllTextAsync(manifestPath,
				ManifestFixtures.ValidManifestJson()
					.Replace("\"manifestVersion\": 1,",
						"\"manifestVersion\": 1,\n\t\"languages\": [\"en\", \"fr\"],",
						StringComparison.Ordinal));

			var result = await PluginPacker.PackAsync(directory, manifestPath, _ => outputPath, force: false);

			Assert.That(result.Success, Is.True, result.FailureMessage);

			var reported = result.Warnings
				.Where(diagnostic => diagnostic.Code == "languages-recomputed")
				.ToList();

			Assert.That(reported, Has.Count.EqualTo(1), "Replacing a declared list must be reported once.");
			Assert.That(reported[0].Message, Does.Contain("de").And.Contain("fr"));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}
}
