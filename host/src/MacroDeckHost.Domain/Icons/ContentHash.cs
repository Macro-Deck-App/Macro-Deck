using System.Security.Cryptography;

namespace MacroDeckHost.Domain.Icons;

public static class ContentHash
{
	public const string Sha256Prefix = "sha256:";

	private const int Sha256HexLength = 64;

	public static string Compute(ReadOnlySpan<byte> content) => Format(SHA256.HashData(content));

	public static string Format(ReadOnlySpan<byte> digest) => Sha256Prefix + Convert.ToHexStringLower(digest);

	public static IncrementalContentHash CreateIncremental() => new();

	public static string? Normalize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var trimmed = value.Trim();
		var hex = trimmed.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase)
			? trimmed[Sha256Prefix.Length..]
			: trimmed;

		if (hex.Length != Sha256HexLength || !IsHex(hex))
		{
			return null;
		}

		return Sha256Prefix + hex.ToLowerInvariant();
	}

	private static bool IsHex(ReadOnlySpan<char> value)
	{
		foreach (var character in value)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}
}
