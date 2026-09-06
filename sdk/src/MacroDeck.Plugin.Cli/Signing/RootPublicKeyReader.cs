using System.Security.Cryptography;
using MacroDeck.Signing;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Plugin.Cli.Signing;

/// <summary>
/// Reads a base64-encoded raw Ed25519 public key file for <c>--root-public</c>, shared by <c>sign</c> and
/// <c>verify</c>. Not part of <see cref="MacroDeck.Signing.SigningError" />'s vocabulary - a broken
/// <c>--root-public</c> value is an unreadable input, not a verdict about a certificate or package.
/// </summary>
internal static class RootPublicKeyReader
{
	public static async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
	{
		string text;
		try
		{
			text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}

		try
		{
			var bytes = Convert.FromBase64String(text.Trim());
			return bytes.Length == Ed25519KeyPair.PublicKeyLength ? bytes : null;
		}
		catch (FormatException)
		{
			return null;
		}
	}

	public static bool IsMacroDeckRoot(byte[] rootPublicKey) =>
		CryptographicOperations.FixedTimeEquals(rootPublicKey, MacroDeckRootKey.PublicKey);
}
