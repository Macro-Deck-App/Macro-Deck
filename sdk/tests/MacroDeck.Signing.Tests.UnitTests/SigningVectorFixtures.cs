using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>Loads the pinned cross-component digest vectors from <c>sdk/tests/fixtures/signing-vectors/</c>,
/// copied next to the test binaries by the csproj's own <c>Content Include</c>. Consumed by both this
/// project and <c>MacroDeck.Plugin.Packaging.Tests.UnitTests</c>, which copies the same files independently
/// - the two are never allowed to read each other's build output.</summary>
internal sealed record DigestVector(string Name, JsonElement Manifest, string DigestUtf8, string DigestSha256)
{
	public string ManifestJson => Manifest.GetRawText();

	/// <summary>Fails loudly if the pinned <see cref="DigestSha256" /> and <see cref="DigestUtf8" /> in the
	/// fixture ever drift apart from each other - a guard against a hand-edited fixture, not against the
	/// implementation under test.</summary>
	public void AssertInternallyConsistent()
	{
		var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DigestUtf8)));
		if (!string.Equals(actual, DigestSha256, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				$"Vector '{Name}': digestSha256 does not match SHA-256(digestUtf8) - the fixture itself is inconsistent.");
		}
	}
}

internal static class SigningVectorFixtures
{
	private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

	public static IReadOnlyList<DigestVector> Load(string fileName)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "signing-vectors", fileName);
		var json = File.ReadAllText(path);
		var vectors = JsonSerializer.Deserialize<List<DigestVector>>(json, _options)!;

		foreach (var vector in vectors)
		{
			vector.AssertInternallyConsistent();
		}

		return vectors;
	}
}
