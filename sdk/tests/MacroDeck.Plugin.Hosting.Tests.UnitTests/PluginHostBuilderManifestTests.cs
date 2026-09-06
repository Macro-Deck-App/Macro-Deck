using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="PluginHostBuilder.Build" /> now reads identity, description and icon from
/// <c>manifest.json</c> at the content root instead of from <c>With*</c> builder calls (#518). Each test
/// points a builder at its own temp content root via <c>--contentRoot</c>, which
/// <see cref="MacroDeckPlugin.CreatePlugin(string[])" /> forwards straight to
/// <c>WebApplicationBuilder.CreateBuilder</c>.
/// </summary>
[TestFixture]
public class PluginHostBuilderManifestTests
{
	private PluginManifestFixture? _fixture;

	[TearDown]
	public void TearDown() => _fixture?.Dispose();

	private PluginHostBuilder CreateBuilder(string manifestJson)
	{
		_fixture = new PluginManifestFixture(manifestJson);
		return _fixture.CreateBuilder();
	}

	[Test]
	public void Metadata_comes_from_the_manifest()
	{
		using var plugin = CreateBuilder("""
										 {
										   "manifestVersion": 1,
										   "id": "com.example.test",
										   "name": "Test Plugin",
										   "version": "2.3.4",
										   "description": "Does things."
										 }
										 """).Build();

		Assert.Multiple(() =>
		{
			Assert.That(plugin.Metadata.Id, Is.EqualTo("com.example.test"));
			Assert.That(plugin.Metadata.Name, Is.EqualTo("Test Plugin"));
			Assert.That(plugin.Metadata.Version, Is.EqualTo("2.3.4"));
			Assert.That(plugin.Metadata.Description, Is.EqualTo("Does things."));
		});
	}

	[Test]
	public void Build_fails_when_the_content_root_has_no_manifest()
	{
		var contentRoot = Directory.CreateTempSubdirectory("macro-deck-plugin-manifest-tests").FullName;
		try
		{
			var exception = Assert.Throws<PluginConfigurationException>(()
				=> MacroDeckPlugin.CreatePlugin(["--contentRoot", contentRoot]).Build());

			Assert.That(exception!.Problems, Has.One.Contains("manifest.json"));
		}
		finally
		{
			Directory.Delete(contentRoot, recursive: true);
		}
	}

	[Test]
	public void Build_fails_when_the_manifest_is_not_valid_json()
	{
		var exception = Assert.Throws<PluginConfigurationException>(()
			=> CreateBuilder("""{ "manifestVersion": 1, "id": "com.example.test" """).Build());

		Assert.That(exception!.Problems, Has.One.Contains("manifest.json"));
	}

	[Test]
	public void Build_fails_when_the_manifest_version_is_not_the_supported_one()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 2,
			  "id": "com.example.test",
			  "name": "Test",
			  "version": "1.0.0"
			}
			""").Build());

		Assert.That(exception!.Problems, Has.One.Contains("manifestVersion"));
	}

	[Test]
	public void Build_fails_when_the_manifest_omits_the_name_or_the_version()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 1,
			  "id": "com.example.test"
			}
			""").Build());

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Problems, Has.One.Matches<string>(problem => problem.Contains("name")));
			Assert.That(exception!.Problems, Has.One.Matches<string>(problem => problem.Contains("version")));
		});
	}

	[Test]
	public void A_declared_icon_that_exists_under_the_content_root_reaches_the_metadata()
	{
		var builder = CreateBuilder("""
									{
									  "manifestVersion": 1,
									  "id": "com.example.test",
									  "name": "Test",
									  "version": "1.0.0",
									  "icon": "assets/icon.png"
									}
									""");
		_fixture!.WriteFile("assets/icon.png", [1, 2, 3]);

		using var plugin = builder.Build();

		Assert.That(plugin.Metadata.IconPath, Is.EqualTo("assets/icon.png"));
	}

	[Test]
	public void Build_fails_when_a_declared_icon_does_not_exist_under_the_content_root()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 1,
			  "id": "com.example.test",
			  "name": "Test",
			  "version": "1.0.0",
			  "icon": "assets/icon.png"
			}
			""").Build());

		Assert.That(exception!.Problems, Has.One.Contains("assets/icon.png"));
	}

	/// <summary>The host rejects a blank icon with <c>InvalidIcon</c>, so accepting it here would let an
	/// artifact build clean and then fail to install - exactly the drift #518 set out to remove.</summary>
	[Test]
	public void Build_fails_when_the_manifest_declares_a_blank_icon()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 1,
			  "id": "com.example.test",
			  "name": "Test",
			  "version": "1.0.0",
			  "icon": ""
			}
			""").Build());

		Assert.That(exception!.Problems, Has.One.Contains("icon"));
	}

	[Test]
	public void Build_fails_for_an_icon_that_escapes_the_content_root_even_when_the_file_is_there()
	{
		var parent = Directory.CreateTempSubdirectory("macro-deck-plugin-manifest-tests").FullName;
		var root = Directory.CreateDirectory(Path.Combine(parent, "root")).FullName;
		var escapingIconPath = Path.Combine(parent, "icon.png");

		try
		{
			File.WriteAllText(Path.Combine(root, PluginManifestFileReader.FileName),
				"""
				{
				  "manifestVersion": 1,
				  "id": "com.example.test",
				  "name": "Test",
				  "version": "1.0.0",
				  "icon": "../icon.png"
				}
				""");

			// The file genuinely exists one level up from the content root - only the shape check, run
			// before existence, must be what rejects this.
			File.WriteAllBytes(escapingIconPath, [1, 2, 3]);

			var exception = Assert.Throws<PluginConfigurationException>(()
				=> MacroDeckPlugin.CreatePlugin(["--contentRoot", root]).Build());

			Assert.That(exception!.Problems, Has.One.Contains("../icon.png"));
		}
		finally
		{
			Directory.Delete(parent, recursive: true);
		}
	}

	[Test]
	public void Unknown_manifest_properties_are_ignored()
	{
		using var plugin = CreateBuilder("""
										 {
										   "manifestVersion": 1,
										   "id": "com.example.test",
										   "name": "Test Plugin",
										   "version": "2.3.4",
										   "description": "Does things.",
										   "entrypoints": { "win-x64": { "executable": "Test.exe" } },
										   "permissions": ["host:variables"],
										   "files": [{ "path": "Test.exe", "sha256": "sha256:aa", "size": 10 }],
										   "publisher": { "name": "Example" },
										   "compatibility": { "macroDeck": ">=3.0.0" },
										   "somethingFromTheFuture": 123
										 }
										 """).Build();

		Assert.Multiple(() =>
		{
			Assert.That(plugin.Metadata.Id, Is.EqualTo("com.example.test"));
			Assert.That(plugin.Metadata.Name, Is.EqualTo("Test Plugin"));
			Assert.That(plugin.Metadata.Version, Is.EqualTo("2.3.4"));
			Assert.That(plugin.Metadata.Description, Is.EqualTo("Does things."));
		});
	}

	[Test]
	public void Build_fails_when_configuration_and_the_manifest_disagree_about_the_id()
	{
		var builder = CreateBuilder("""
									{
									  "manifestVersion": 1,
									  "id": "com.example.test",
									  "name": "Test",
									  "version": "1.0.0"
									}
									""");
		builder.Configuration[$"{PluginHostOptions.SectionName}:Id"] = "com.example.other";

		var exception = Assert.Throws<PluginConfigurationException>(() => builder.Build());

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Problems, Has.One.Contains("com.example.test"));
			Assert.That(exception!.Problems, Has.One.Contains("com.example.other"));
		});
	}

	[Test]
	public void Build_reports_every_problem_at_once()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 1,
			  "id": "bad id",
			  "name": "Test",
			  "version": "1.0.0",
			  "icon": "no-such-icon.png"
			}
			""").Build());

		// The whole point of collecting: an author fixing two mistakes should need one run, not two.
		Assert.That(exception!.Problems, Has.Count.EqualTo(2));
	}

	/// <summary>The host rejects a too-long declaredName built from this same manifest name (see
	/// ProtocolLimits.MaxDeclaredNameLength) - a self-registering plugin is never seen by the host's own
	/// manifest reader, so this has to be caught here at build time instead of failing every handshake.</summary>
	[Test]
	public void Build_fails_when_the_manifest_name_exceeds_the_declared_name_limit()
	{
		var overLongName = new string('a', 129);
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder($$"""
			  {
			    "manifestVersion": 1,
			    "id": "com.example.test",
			    "name": "{{overLongName}}",
			    "version": "1.0.0"
			  }
			  """).Build());

		Assert.That(exception!.Problems, Has.One.Matches<string>(problem => problem.Contains("128")));
	}

	[Test]
	public void Build_fails_when_the_manifest_name_contains_a_control_character()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 1,
			  "id": "com.example.test",
			  "name": "Test\nPlugin",
			  "version": "1.0.0"
			}
			""").Build());

		Assert.That(exception!.Problems, Has.One.Matches<string>(problem => problem.Contains("control character")));
	}

	[Test]
	public void Build_fails_when_the_manifest_id_is_not_a_reverse_domain_id()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => CreateBuilder("""
			{
			  "manifestVersion": 1,
			  "id": "not-reverse-domain",
			  "name": "Test",
			  "version": "1.0.0"
			}
			""").Build());

		Assert.That(exception!.Problems, Has.One.Contains("'not-reverse-domain'"));
	}
}
