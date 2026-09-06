using MacroDeck.Plugin.Protocol.Assets;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Assets;

/// <summary>
/// <see cref="AssetContentHash.IsValid" /> is the one gate standing between a plugin-supplied content
/// hash and a file path built from it (<c>PluginAssetDiskCache</c>) - see that type's remarks. Every
/// rejection case here is a shape that would otherwise reach <c>Path.Combine</c> unchecked.
/// </summary>
[TestFixture]
public class AssetContentHashTests
{
	[Test]
	public void A_hash_this_type_computed_is_valid()
	{
		var hash = AssetContentHash.Compute([1, 2, 3]);

		Assert.That(AssetContentHash.IsValid(hash), Is.True);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("sha256:")]
	[TestCase("sha256:../../../../etc/passwd")]
	[TestCase("sha256:/etc/passwd")]
	[TestCase("md5:d41d8cd98f00b204e9800998ecf8427e")]
	// Upper-case hex, and one character short/long of the required 64.
	[TestCase("SHA256:0000000000000000000000000000000000000000000000000000000000000000")]
	public void A_malformed_hash_is_rejected(string? candidate) =>
		Assert.That(AssetContentHash.IsValid(candidate), Is.False);

	[Test]
	public void A_hash_with_uppercase_hex_is_rejected()
	{
		var upper = "sha256:" + new string('A', 64);

		Assert.That(AssetContentHash.IsValid(upper), Is.False);
	}

	[Test]
	public void A_hash_one_character_short_is_rejected()
	{
		var tooShort = "sha256:" + new string('a', 63);

		Assert.That(AssetContentHash.IsValid(tooShort), Is.False);
	}

	[Test]
	public void A_hash_one_character_long_is_rejected()
	{
		var tooLong = "sha256:" + new string('a', 65);

		Assert.That(AssetContentHash.IsValid(tooLong), Is.False);
	}

	[Test]
	public void A_well_formed_hash_is_accepted()
	{
		var wellFormed = "sha256:" + new string('a', 64);

		Assert.That(AssetContentHash.IsValid(wellFormed), Is.True);
	}
}
