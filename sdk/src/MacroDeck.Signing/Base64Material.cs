using System.Security.Cryptography;

namespace MacroDeck.Signing;

/// <summary>Decodes fixed-length base64 key and signature material, zeroing any wrong-length result before
/// discarding it.</summary>
internal static class Base64Material
{
	public static byte[]? TryDecode(string? text, int expectedLength)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		byte[] buffer;
		try
		{
			buffer = Convert.FromBase64String(text.Trim());
		}
		catch (FormatException)
		{
			return null;
		}

		if (buffer.Length != expectedLength)
		{
			CryptographicOperations.ZeroMemory(buffer);
			return null;
		}

		return buffer;
	}
}
