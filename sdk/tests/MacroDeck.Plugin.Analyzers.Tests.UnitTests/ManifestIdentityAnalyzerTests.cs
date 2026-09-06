using System.Globalization;
using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class ManifestIdentityAnalyzerTests
{
	[Test]
	public async Task Fires_when_the_manifest_declares_an_invalid_id()
	{
		const string manifest = """
								{
								  "manifestVersion": 1,
								  "id": "NotReverseDomain",
								  "name": "My Plugin",
								  "version": "1.0.0"
								}
								""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new ManifestIdentityAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1001"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], manifest), Is.EqualTo("\"NotReverseDomain\""));
	}

	/// <summary>
	/// Also proves nested keys never confuse this rule - a real manifest always has an "entrypoints"
	/// object (and often "publisher"), each with its own "id"-shaped or unrelated keys one level down
	/// that must never be mistaken for the plugin's own top-level id/name/version.
	/// </summary>
	[Test]
	public async Task Does_not_fire_when_the_manifest_declares_a_valid_id_name_and_version()
	{
		const string manifest = """
								{
								  "manifestVersion": 1,
								  "id": "com.example.my-plugin",
								  "name": "My Plugin",
								  "version": "1.0.0",
								  "publisher": { "id": "com.example.not-the-plugin-id", "name": "Example" },
								  "entrypoints": {
								    "win-x64": { "executable": "MyPlugin.exe" }
								  }
								}
								""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new ManifestIdentityAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Fires_when_the_manifest_declares_no_name()
	{
		const string manifest = """
								{
								  "manifestVersion": 1,
								  "id": "com.example.my-plugin",
								  "version": "1.0.0"
								}
								""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new ManifestIdentityAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1001"));
		Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture), Does.Contain("\"name\""));
	}

	[Test]
	public async Task Fires_when_the_manifest_declares_an_empty_name()
	{
		const string manifest = """
								{
								  "manifestVersion": 1,
								  "id": "com.example.my-plugin",
								  "name": "",
								  "version": "1.0.0"
								}
								""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new ManifestIdentityAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1001"));
		Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture), Does.Contain("\"name\""));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], manifest), Is.EqualTo("\"\""));
	}

	[Test]
	public async Task Fires_when_the_manifest_declares_no_version()
	{
		const string manifest = """
								{
								  "manifestVersion": 1,
								  "id": "com.example.my-plugin",
								  "name": "My Plugin"
								}
								""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new ManifestIdentityAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1001"));
		Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture), Does.Contain("\"version\""));
	}

	[Test]
	public async Task Does_not_fire_on_an_unrelated_additional_file()
	{
		const string appSettings = """{ "Logging": { "LogLevel": { "Default": "Information" } } }""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("appsettings.json",
			appSettings,
			new ManifestIdentityAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
