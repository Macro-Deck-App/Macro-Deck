using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

/// <summary>
/// <see cref="PluginManifestJson.Options"/> is the single JSON configuration every manifest is read
/// through - moved here unchanged from the host's own <c>PersistenceJsonOptions.Default</c>, which now
/// returns this very instance. A silent divergence between them would change which manifests parse,
/// and it would only surface when a user's plugin stops installing.
/// <para>
/// This asserts the reader's observable behaviour only through <see cref="IPluginManifestReader"/>'s
/// result, never by inspecting <see cref="PluginManifestJson.Options"/>'s properties directly - a
/// property-by-property comparison would only prove the options match themselves, not that a real
/// manifest still reads the way it did before the move.
/// </para>
/// </summary>
[TestFixture]
public class PluginManifestJsonTests
{
	private const string PluginId = "com.suchbyte.test-plugin";
	private const string Version = "1.0.0";

	private readonly PluginManifestReader _reader = new();

	[Test]
	public void The_shared_options_preserve_the_readers_documented_parsing_behaviour()
	{
		Assert.Multiple(() =>
		{
			AssertCamelCaseManifestReadsSuccessfully();
			AssertPascalCaseManifestReadsSuccessfully();
			AssertAStringEnumValueParsesToItsMember();
			AssertTrailingCommasAreRejectedJustLikeToday();
		});
	}

	/// <summary>The shape every manifest the host writes or a plugin author hand-authors actually uses.
	/// </summary>
	private void AssertCamelCaseManifestReadsSuccessfully()
	{
		const string json = """
							{
								"manifestVersion": 1,
								"id": "com.suchbyte.test-plugin",
								"name": "Test Plugin",
								"version": "1.0.0",
								"entrypoints": { "linux-x64": { "executable": "TestPlugin" } }
							}
							""";

		var result = _reader.ReadFromJson(json, versionDirectory: null, PluginId, Version);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Id, Is.EqualTo(PluginId));
		Assert.That(result.Manifest.Name, Is.EqualTo("Test Plugin"));
		Assert.That(result.Manifest.Entrypoints["linux-x64"].Executable, Is.EqualTo("TestPlugin"));
	}

	/// <summary><see cref="System.Text.Json.JsonSerializerOptions.PropertyNameCaseInsensitive"/> is what
	/// lets an older, hand-edited PascalCase manifest keep reading the same as its camelCase equivalent.
	/// </summary>
	private void AssertPascalCaseManifestReadsSuccessfully()
	{
		const string json = """
							{
								"ManifestVersion": 1,
								"Id": "com.suchbyte.test-plugin",
								"Name": "Test Plugin",
								"Version": "1.0.0",
								"Entrypoints": { "linux-x64": { "Executable": "TestPlugin" } }
							}
							""";

		var result = _reader.ReadFromJson(json, versionDirectory: null, PluginId, Version);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Id, Is.EqualTo(PluginId));
		Assert.That(result.Manifest.Name, Is.EqualTo("Test Plugin"));
		Assert.That(result.Manifest.Entrypoints["linux-x64"].Executable, Is.EqualTo("TestPlugin"));
	}

	/// <summary><see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> is what lets
	/// <c>"kind": "FrameworkDependent"</c> - the only spelling a hand-authored manifest ever uses - bind
	/// to <see cref="PluginEntrypointRuntimeKind"/> instead of demanding its raw numeric value.</summary>
	private void AssertAStringEnumValueParsesToItsMember()
	{
		const string json = """
							{
								"manifestVersion": 1,
								"id": "com.suchbyte.test-plugin",
								"name": "Test Plugin",
								"version": "1.0.0",
								"entrypoints": {
									"linux-x64": {
										"executable": "TestPlugin.dll",
										"runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
									}
								}
							}
							""";

		var result = _reader.ReadFromJson(json, versionDirectory: null, PluginId, Version);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Entrypoints["linux-x64"].Runtime!.Kind,
			Is.EqualTo(PluginEntrypointRuntimeKind.FrameworkDependent));
	}

	/// <summary>
	/// Perhaps surprisingly, this is a rejection today, not a successful read: <see cref="PluginManifestReader"/>
	/// probes <c>manifestVersion</c> with a bare, unconfigured <see cref="System.Text.Json.JsonDocument"/>
	/// parse before it ever reaches <see cref="PluginManifestJson.Options"/>, and that probe does not share
	/// the options' <c>AllowTrailingCommas</c>. This pins that combination exactly as it behaves today -
	/// not as this test's author might expect it to - so a future change that "fixes" the probe to
	/// tolerate trailing commas (or that regresses the options into no longer tolerating them elsewhere)
	/// shows up here either way.
	/// </summary>
	private void AssertTrailingCommasAreRejectedJustLikeToday()
	{
		const string json = """
							{
								"manifestVersion": 1,
								"id": "com.suchbyte.test-plugin",
								"name": "Test Plugin",
								"version": "1.0.0",
								"entrypoints": { "linux-x64": { "executable": "TestPlugin", }, },
							}
							""";

		var result = _reader.ReadFromJson(json, versionDirectory: null, PluginId, Version);

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Is.EqualTo(PluginManifestError.Malformed));
	}
}
