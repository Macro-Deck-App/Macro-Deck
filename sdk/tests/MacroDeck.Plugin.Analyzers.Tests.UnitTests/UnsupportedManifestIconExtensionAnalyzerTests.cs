using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class UnsupportedManifestIconExtensionAnalyzerTests
{
	private static string ManifestWithIcon(string iconPath) => $$"""
																 {
																   "manifestVersion": 1,
																   "id": "com.example.my-plugin",
																   "name": "My Plugin",
																   "version": "1.0.0",
																   "icon": "{{iconPath}}"
																 }
																 """;

	[Test]
	public async Task Fires_on_an_unsupported_extension()
	{
		var manifest = ManifestWithIcon("assets/icon.gif");

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new UnsupportedManifestIconExtensionAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1003"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], manifest),
			Is.EqualTo("\"assets/icon.gif\""));
	}

	[TestCase("assets/icon.svg")]
	[TestCase("assets/icon.png")]
	[TestCase("assets/icon.jpg")]
	[TestCase("assets/icon.jpeg")]
	[TestCase("assets/icon.webp")]
	public async Task Does_not_fire_on_a_supported_extension(string iconPath)
	{
		var manifest = ManifestWithIcon(iconPath);

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new UnsupportedManifestIconExtensionAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Does_not_fire_when_the_extension_case_differs()
	{
		// Matching is case-insensitive: a manifest may name Assets/Logo.PNG on a case-insensitive
		// filesystem and still be the same, supported icon.
		var manifest = ManifestWithIcon("assets/Icon.PNG");

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new UnsupportedManifestIconExtensionAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Does_not_fire_when_the_manifest_declares_no_icon()
	{
		const string manifest = """
								{
								  "manifestVersion": 1,
								  "id": "com.example.my-plugin",
								  "name": "My Plugin",
								  "version": "1.0.0"
								}
								""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("manifest.json",
			manifest,
			new UnsupportedManifestIconExtensionAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
