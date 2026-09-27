namespace MacroDeck.Plugin.Packaging.IconPacks;

/// <summary>
/// Structural bounds on a <c>.macroDeckIconPack</c>, shared by everything that writes, signs, verifies
/// or imports one, so a pack one of them accepts is never refused by another. An entry is a raw ZIP
/// entry, directory entries included. The two bounds apply independently: a pack of icons with a
/// single image each, or with non-ASCII names (written as <c>\uXXXX</c>), reaches the manifest bound
/// long before the entry bound.
/// </summary>
public static class IconPackArchiveLimits
{
	private const int MaxSignatureEntries = 4;

	private const int MaxSignatureBytes = 64 * 1024;

	/// <summary>The most entries a readable pack may have, signed or not, signature files included.</summary>
	public const int MaxEntries = 30_000;

	/// <summary>The largest <c>pack.json</c> a readable pack may carry, signed or not.</summary>
	public const int MaxManifestBytes = 32 * 1024 * 1024;

	/// <summary>
	/// The most entries a pack may have when it is produced, before signing: the signer adds up to
	/// four files (<c>certificate.json</c>/<c>.sig</c>, <c>issuer.json</c>/<c>.sig</c>), and the signed
	/// pack must still be within <see cref="MaxEntries"/>.
	/// </summary>
	public const int MaxUnsignedEntries = MaxEntries - MaxSignatureEntries;

	/// <summary>
	/// The largest <c>pack.json</c> a pack may carry when it is produced, before signing, leaving room
	/// for the signature object within <see cref="MaxManifestBytes"/>. The signer rewrites the manifest,
	/// so this holds for <c>pack.json</c> in the form Macro Deck writes it (indented, non-ASCII escaped)
	/// with the same or shorter line endings as the signing machine; a compact or unescaped manifest, or
	/// one written with <c>\n</c> and signed on Windows, grows when signed and may be refused.
	/// </summary>
	public const int MaxUnsignedManifestBytes = MaxManifestBytes - MaxSignatureBytes;
}
