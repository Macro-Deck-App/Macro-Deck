using System.IO.Compression;

namespace MacroDeckHost.Infrastructure.Backups;

public static class BackupEntryCompression
{
	private static readonly HashSet<string> _alreadyCompressed = new(StringComparer.OrdinalIgnoreCase)
	{
		".png",
		".jpg",
		".jpeg",
		".gif",
		".webp",
		".avif",
		".heic",
		".ico",
		".mp3",
		".m4a",
		".ogg",
		".opus",
		".flac",
		".mp4",
		".m4v",
		".webm",
		".mov",
		".woff",
		".woff2",
		".zip",
		".gz",
		".bz2",
		".xz",
		".zst",
		".7z",
		".rar",
		".nupkg",
		".jar"
	};

	public static CompressionLevel For(string relativePath)
		=> _alreadyCompressed.Contains(Path.GetExtension(relativePath))
			? CompressionLevel.NoCompression
			: CompressionLevel.Fastest;
}
