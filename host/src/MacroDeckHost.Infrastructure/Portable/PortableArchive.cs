using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Packaging;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Infrastructure.Portable;

public enum PortableReadStatus
{
	Success,
	InvalidArchive,
	UnsupportedVersion,
	PasswordRequired,
	WrongPassword
}

public sealed record PortableReadOutcome(
	PortableReadStatus Status,
	PortableArchiveManifest? Manifest,
	PortableContent? Content,
	IReadOnlyList<PortableIconFile> Icons);

public static class PortableArchive
{
	private const string ManifestEntry = "manifest.json";
	private const string ContentEntry = "content.json";
	private const string EncryptedPayloadEntry = "payload.enc";
	private const int MaxEntries = 20_000;
	private const long MaxManifestBytes = 64 * 1024;
	private const long MaxContentBytes = 64L * 1024 * 1024;
	private const long MaxPayloadBytes = 192L * 1024 * 1024;
	private const long MaxIconBytes = 16L * 1024 * 1024;
	private const long MaxTotalIconBytes = 128L * 1024 * 1024;

	public static byte[] Write(PortableArchiveManifest manifest,
		PortableContent content,
		IReadOnlyList<PortableIconFile> icons,
		string? password)
	{
		if (!string.IsNullOrEmpty(password))
		{
			manifest.Encryption = PortableArchiveCrypto.CreateParameters();

			// The payload (and its digest) does not exist until after encryption, and encryption needs the
			// manifest bytes as associated data - so an encrypted archive declares no files at all. The same
			// bytes are written to disk and used as associated data, which is what lets a released Macro
			// Deck decrypt an archive this build wrote.
			var manifestBytes = SerializeManifest(manifest);

			var innerBytes = WriteBundle(content, icons);
			var payload = PortableArchiveCrypto.Encrypt(innerBytes, password, manifest.Encryption, manifestBytes);

			using var outer = new MemoryStream();
			using (var zip = new ZipArchive(outer, ZipArchiveMode.Create, leaveOpen: true))
			{
				WriteManifest(zip, manifestBytes);
				var entry = zip.CreateEntry(EncryptedPayloadEntry, CompressionLevel.NoCompression);
				using var stream = entry.Open();
				stream.Write(payload);
			}

			return outer.ToArray();
		}

		manifest.Encryption = null;
		using var buffer = new MemoryStream();
		using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			manifest.Files = WriteBundleEntries(zip, content, icons);
			WriteManifest(zip, SerializeManifest(manifest));
		}

		return buffer.ToArray();
	}

	public static PortableArchiveManifest? ReadManifest(byte[] archiveBytes)
	{
		try
		{
			using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
			return ReadManifest(zip);
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	public static PortableReadOutcome Read(byte[] archiveBytes, string? password)
	{
		ZipArchive zip;
		try
		{
			zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
		}
		catch (InvalidDataException)
		{
			return Fail(PortableReadStatus.InvalidArchive);
		}

		using (zip)
		{
			if (zip.Entries.Count > MaxEntries)
			{
				return Fail(PortableReadStatus.InvalidArchive);
			}

			var manifest = ReadManifest(zip);
			if (manifest is null)
			{
				return Fail(PortableReadStatus.InvalidArchive);
			}

			if (manifest.FormatVersion > PortableArchiveManifest.CurrentFormatVersion)
			{
				return new PortableReadOutcome(PortableReadStatus.UnsupportedVersion, manifest, null, []);
			}

			return manifest.Encryption is null
				? ReadPlain(zip, manifest)
				: ReadEncrypted(zip, manifest, password);
		}
	}

	private static PortableReadOutcome ReadPlain(ZipArchive zip, PortableArchiveManifest manifest)
	{
		var content = ReadContent(zip);
		if (content is null)
		{
			return Fail(PortableReadStatus.InvalidArchive);
		}

		var icons = ReadIcons(zip);
		return icons is null
			? Fail(PortableReadStatus.InvalidArchive)
			: new PortableReadOutcome(PortableReadStatus.Success, manifest, content, icons);
	}

	private static PortableReadOutcome ReadEncrypted(ZipArchive zip,
		PortableArchiveManifest manifest,
		string? password)
	{
		if (string.IsNullOrEmpty(password))
		{
			return new PortableReadOutcome(PortableReadStatus.PasswordRequired, manifest, null, []);
		}

		var payloadEntry = zip.GetEntry(EncryptedPayloadEntry);
		if (payloadEntry is null)
		{
			return Fail(PortableReadStatus.InvalidArchive);
		}

		var payload = ReadEntry(payloadEntry, MaxPayloadBytes);
		if (payload is null)
		{
			return Fail(PortableReadStatus.InvalidArchive);
		}

		var manifestEntry = zip.GetEntry(ManifestEntry);
		var manifestBytes = manifestEntry is null ? null : ReadEntry(manifestEntry, MaxManifestBytes);
		if (manifestBytes is null)
		{
			return Fail(PortableReadStatus.InvalidArchive);
		}

		var status = PortableArchiveCrypto.TryDecrypt(payload,
			manifest.Encryption!,
			manifestBytes,
			password,
			out var innerBytes);
		switch (status)
		{
			case PortableDecryptResult.WrongPassword:
				return new PortableReadOutcome(PortableReadStatus.WrongPassword, manifest, null, []);
			case PortableDecryptResult.UnsupportedAlgorithm:
				return new PortableReadOutcome(PortableReadStatus.UnsupportedVersion, manifest, null, []);
			case PortableDecryptResult.Corrupt:
				return Fail(PortableReadStatus.InvalidArchive);
		}

		try
		{
			using var inner = new ZipArchive(new MemoryStream(innerBytes), ZipArchiveMode.Read);
			if (inner.Entries.Count > MaxEntries)
			{
				return Fail(PortableReadStatus.InvalidArchive);
			}

			var content = ReadContent(inner);
			if (content is null)
			{
				return Fail(PortableReadStatus.InvalidArchive);
			}

			var icons = ReadIcons(inner);
			return icons is null
				? Fail(PortableReadStatus.InvalidArchive)
				: new PortableReadOutcome(PortableReadStatus.Success, manifest, content, icons);
		}
		catch (InvalidDataException)
		{
			return Fail(PortableReadStatus.InvalidArchive);
		}
	}

	private static byte[] WriteBundle(PortableContent content, IReadOnlyList<PortableIconFile> icons)
	{
		using var buffer = new MemoryStream();
		using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			// The inner bundle is opaque once encrypted - nothing reads content.json or the icon entries
			// independently - so the per-entry digests this returns are not needed here.
			WriteBundleEntries(zip, content, icons);
		}

		return buffer.ToArray();
	}

	private static List<PackageFileDigest> WriteBundleEntries(ZipArchive zip,
		PortableContent content,
		IReadOnlyList<PortableIconFile> icons)
	{
		var files = new List<PackageFileDigest>();

		var contentBytes = JsonSerializer.SerializeToUtf8Bytes(content, PersistenceJsonOptions.Default);
		var contentEntry = zip.CreateEntry(ContentEntry, CompressionLevel.Optimal);
		using (var stream = contentEntry.Open())
		{
			stream.Write(contentBytes);
		}

		files.Add(ComputeFileDigest(ContentEntry, contentBytes));

		foreach (var icon in icons)
		{
			var path = $"icons/{icon.IconId}/{icon.Variant}.webp";
			var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
			using (var stream = entry.Open())
			{
				stream.Write(icon.Bytes);
			}

			files.Add(ComputeFileDigest(path, icon.Bytes));
		}

		files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
		return files;
	}

	private static PackageFileDigest ComputeFileDigest(string path, byte[] content)
		=> new() { Path = path, Sha256 = ContentHash.Compute(content), Size = content.Length };

	private static byte[] SerializeManifest(PortableArchiveManifest manifest)
		=> JsonSerializer.SerializeToUtf8Bytes(manifest, PersistenceJsonOptions.Default);

	private static void WriteManifest(ZipArchive zip, byte[] manifestBytes)
	{
		var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
		using var stream = entry.Open();
		stream.Write(manifestBytes);
	}

	private static PortableArchiveManifest? ReadManifest(ZipArchive zip)
	{
		var entry = zip.GetEntry(ManifestEntry);
		if (entry is null)
		{
			return null;
		}

		var bytes = ReadEntry(entry, MaxManifestBytes);
		if (bytes is null)
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<PortableArchiveManifest>(bytes, PersistenceJsonOptions.Default);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static PortableContent? ReadContent(ZipArchive zip)
	{
		var entry = zip.GetEntry(ContentEntry);
		if (entry is null)
		{
			return null;
		}

		var bytes = ReadEntry(entry, MaxContentBytes);
		if (bytes is null)
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<PortableContent>(bytes, PersistenceJsonOptions.Default);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static List<PortableIconFile>? ReadIcons(ZipArchive zip)
	{
		var icons = new List<PortableIconFile>();
		var budget = MaxTotalIconBytes;
		foreach (var entry in zip.Entries)
		{
			var segments = entry.FullName.Replace('\\', '/').Split('/');
			if (segments.Length != 3 ||
				!segments[0].Equals("icons", StringComparison.OrdinalIgnoreCase) ||
				!Guid.TryParse(segments[1], out var iconId))
			{
				continue;
			}

			var fileName = segments[2];
			if (!fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var variant = Path.GetFileNameWithoutExtension(fileName);
			var isMaster = variant.Equals(IconVariants.Master, StringComparison.OrdinalIgnoreCase);
			if (!isMaster && !int.TryParse(variant, NumberStyles.None, CultureInfo.InvariantCulture, out _))
			{
				continue;
			}

			var bytes = ReadEntry(entry, Math.Min(MaxIconBytes, budget));
			if (bytes is null)
			{
				return null;
			}

			budget -= bytes.Length;
			icons.Add(new PortableIconFile(iconId, isMaster ? IconVariants.Master : variant, bytes));
		}

		return icons;
	}

	private static byte[]? ReadEntry(ZipArchiveEntry entry, long limit)
	{
		if (entry.Length > limit)
		{
			return null;
		}

		try
		{
			using var stream = entry.Open();
			using var buffer = new MemoryStream();
			var chunk = new byte[81_920];
			int read;
			while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
			{
				if (buffer.Length + read > limit)
				{
					return null;
				}

				buffer.Write(chunk, 0, read);
			}

			return buffer.ToArray();
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	private static PortableReadOutcome Fail(PortableReadStatus status)
		=> new(status, null, null, []);
}
