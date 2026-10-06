using System.Buffers.Binary;
using MacroDeckHost.Domain.Icons;
using SkiaSharp;

namespace MacroDeckHost.Infrastructure.Rendering;

internal sealed record UserFontFace(
	string Family,
	int Weight,
	int Width,
	SKFontStyleSlant Slant,
	bool RemoteRenderable);

internal static class UserFontFiles
{
	public const long MaxFileBytes = 32L * 1024 * 1024;
	private const int FontIdLength = 16;
	private const uint CollectionSignature = 0x74746366;

	private static readonly HashSet<string> Extensions = new([".ttf", ".otf"], StringComparer.OrdinalIgnoreCase);

	public static bool HasFontExtension(string fileName) => Extensions.Contains(Path.GetExtension(fileName));

	public static string FontIdOf(string contentHash) =>
		contentHash[ContentHash.Sha256Prefix.Length..][..FontIdLength];

	public static bool IsFontId(string? value) =>
		value is { Length: FontIdLength } && value.All(char.IsAsciiHexDigitLower);

	public static IEnumerable<string> Enumerate(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return [];
		}

		try
		{
			return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
				.Where(path => HasFontExtension(path) && IsFontId(Path.GetFileNameWithoutExtension(path)))
				.Order(StringComparer.Ordinal)
				.ToList();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	public static UserFontFace? Inspect(byte[] bytes)
	{
		if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(bytes) == CollectionSignature)
		{
			return null;
		}

		using var typeface = Open(bytes);
		if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName))
		{
			return null;
		}

		return new UserFontFace(typeface.FamilyName,
			typeface.FontWeight,
			typeface.FontWidth,
			typeface.FontSlant,
			SfntFaceExtractor.CanExtract(typeface));
	}

	public static SKTypeface? Open(byte[] bytes)
	{
		using var data = SKData.CreateCopy(bytes);
		return SKTypeface.FromData(data);
	}
}
