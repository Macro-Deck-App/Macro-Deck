using System.Security.Cryptography;

namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>
/// The one place an <c>asset.*</c> content hash string is produced. Shared by the SDK uploader (which
/// computes it before <c>asset.begin</c>) and the host receiver (which recomputes it over the
/// reassembled bytes and compares) so the two ends can never silently drift onto different formats.
/// Written as <c>sha256:&lt;lowercase hex&gt;</c>, matching the convention
/// <c>MacroDeckHost.Domain.Icons.ContentHash</c> already uses for locally-imported icons - this type
/// exists separately because the protocol project must not depend on the host's domain layer.
/// </summary>
public static class AssetContentHash
{
	public const string Sha256Prefix = "sha256:";

	/// <summary>Length of the hex portion: a SHA-256 digest is 32 bytes, i.e. 64 hex characters.</summary>
	private const int HexLength = 64;

	public static string Compute(ReadOnlySpan<byte> content) =>
		Sha256Prefix + Convert.ToHexStringLower(SHA256.HashData(content));

	/// <summary>
	/// True only for exactly <see cref="Sha256Prefix" /> followed by 64 lowercase hex characters - the one
	/// shape <see cref="Compute" /> ever produces. A plugin-supplied content hash (an icon or artwork
	/// describe reply, for instance) must pass this before it is ever used to build a file path: it is the
	/// only thing standing between an untrusted string and <c>Path.Combine</c>, so anything else - a
	/// relative path segment, an absolute path, upper-case hex, a different algorithm prefix - is rejected
	/// here rather than reaching the disk cache.
	/// </summary>
	public static bool IsValid(string? contentHash)
	{
		if (contentHash is null ||
			contentHash.Length != Sha256Prefix.Length + HexLength ||
			!contentHash.StartsWith(Sha256Prefix, StringComparison.Ordinal))
		{
			return false;
		}

		for (var i = Sha256Prefix.Length; i < contentHash.Length; i++)
		{
			var c = contentHash[i];
			var isLowerHex = c is >= '0' and <= '9' or >= 'a' and <= 'f';
			if (!isLowerHex)
			{
				return false;
			}
		}

		return true;
	}
}
