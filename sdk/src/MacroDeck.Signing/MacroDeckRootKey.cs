namespace MacroDeck.Signing;

/// <summary>
/// The public Ed25519 key of Macro Deck's offline root, the trust anchor every
/// <see cref="Certificates.SigningCertificate"/> chains to. Macro Deck itself never holds the matching
/// private key at runtime; it exists only in the offline key-generation tool.
/// </summary>
public static class MacroDeckRootKey
{
	/// <summary>The root's raw 32-byte Ed25519 public key, base64-encoded.</summary>
	public const string PublicKeyBase64 = "14ZjHOKbbOh3x2VUAqy5cyOwfS5mgJ2R2gUSGjW13cg=";

	/// <summary>The decoded length of <see cref="PublicKeyBase64"/>.</summary>
	public const int PublicKeyLength = 32;

	private static readonly byte[] _publicKey = Convert.FromBase64String(PublicKeyBase64);

	/// <summary>The decoded root public key.</summary>
	public static ReadOnlySpan<byte> PublicKey => _publicKey;
}
