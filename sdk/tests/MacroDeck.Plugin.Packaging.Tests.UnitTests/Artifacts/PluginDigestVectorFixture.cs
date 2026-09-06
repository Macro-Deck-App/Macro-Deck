using System.Text.Json;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts;

/// <summary>Loads the pinned plugin digest vectors from <c>sdk/tests/fixtures/signing-vectors/</c>, copied
/// next to this project's own test binaries by its csproj's <c>Content Include</c> - independently of
/// <c>MacroDeck.Signing.Tests.UnitTests</c>' copy, which
/// <see cref="MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts.PluginArtifactDigestTests" /> never reads.</summary>
internal sealed record PluginDigestVector(string Name, JsonElement Manifest, string DigestUtf8, string DigestSha256)
{
	public string ManifestJson => Manifest.GetRawText();

	private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

	public static IReadOnlyList<PluginDigestVector> Load()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "signing-vectors", "plugin-digest-vectors.json");
		var json = File.ReadAllText(path);
		return JsonSerializer.Deserialize<List<PluginDigestVector>>(json, _options)!;
	}
}
