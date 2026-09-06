using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MacroDeck.Signing.TestSupport;

/// <summary>
/// Hand-built ZIP archives for <see cref="MacroDeck.Signing.Packages.PackageSigner" />/<see cref="MacroDeck.Signing.Packages.PackageVerifier" />
/// tests - written entry-by-entry with <see cref="System.IO.Compression.ZipArchive" />, never through
/// <c>PluginPacker</c> or any other Macro Deck packing code, so a test built from one of these can never
/// merely confirm the signing library agrees with the rest of the codebase.
/// </summary>
public static class PackageArchiveFixtures
{
	public sealed record DeclaredFile(string Path, string Content);

	/// <summary>Writes a <c>.macroDeckPlugin</c> archive at <paramref name="path" /> with a manifest
	/// declaring exactly <paramref name="files" />, each backed by a real entry with the matching SHA-256
	/// and size. <paramref name="extraManifestProperties" /> lets a caller splice in unknown top-level JSON
	/// or (with an empty <paramref name="files" /> array) omit <c>files</c> entirely.</summary>
	public static string CreatePluginArchive(string path,
		string id,
		string version,
		IReadOnlyList<DeclaredFile> files,
		string entrypointExecutable = "app",
		IReadOnlyDictionary<string, object>? extraManifestProperties = null,
		IReadOnlyList<string>? permissions = null,
		bool declareFiles = true)
	{
		var manifest = new JsonObject
		{
			["manifestVersion"] = 1,
			["id"] = id,
			["name"] = "Test Plugin",
			["version"] = version,
			["entrypoints"] = new JsonObject
			{
				["linux-x64"] = new JsonObject { ["executable"] = entrypointExecutable }
			}
		};

		if (permissions is not null)
		{
			manifest["permissions"] = new JsonArray(permissions.Select(p => (JsonNode)p!).ToArray());
		}

		if (declareFiles)
		{
			manifest["files"] = new JsonArray(files.Select(file => (JsonNode)new JsonObject
			{
				["path"] = file.Path,
				["sha256"] = "sha256:" + Sha256Hex(file.Content),
				["size"] = Encoding.UTF8.GetByteCount(file.Content)
			}).ToArray());
		}

		if (extraManifestProperties is not null)
		{
			foreach (var (key, value) in extraManifestProperties)
			{
				manifest[key] = JsonValue.Create(value);
			}
		}

		using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

		WriteEntry(archive, "manifest.json", manifest.ToJsonString());
		foreach (var file in files)
		{
			WriteEntry(archive, file.Path, file.Content);
		}

		return path;
	}

	public static string CreateIconPackArchive(string path, string id, string name, IReadOnlyList<DeclaredFile> files)
	{
		var manifest = new JsonObject
		{
			["id"] = id,
			["name"] = name,
			["files"] = new JsonArray(files.Select(file => (JsonNode)new JsonObject
			{
				["path"] = file.Path,
				["sha256"] = "sha256:" + Sha256Hex(file.Content),
				["size"] = Encoding.UTF8.GetByteCount(file.Content)
			}).ToArray())
		};

		using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

		WriteEntry(archive, "pack.json", manifest.ToJsonString());
		foreach (var file in files)
		{
			WriteEntry(archive, file.Path, file.Content);
		}

		return path;
	}

	public static string CreatePortableArchive(string path,
		string kind,
		object formatVersion,
		IReadOnlyList<DeclaredFile> files)
	{
		var manifest = new JsonObject
		{
			["kind"] = kind,
			["formatVersion"] = JsonValue.Create(formatVersion),
			["files"] = new JsonArray(files.Select(file => (JsonNode)new JsonObject
			{
				["path"] = file.Path,
				["sha256"] = "sha256:" + Sha256Hex(file.Content),
				["size"] = Encoding.UTF8.GetByteCount(file.Content)
			}).ToArray())
		};

		using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

		WriteEntry(archive, "manifest.json", manifest.ToJsonString());
		foreach (var file in files)
		{
			WriteEntry(archive, file.Path, file.Content);
		}

		return path;
	}

	/// <summary>Rewrites one entry of an existing archive in place, keeping every other entry byte-for-byte.
	/// Used to tamper with a signed artifact after the fact.</summary>
	public static void ReplaceEntry(string archivePath, string entryName, string newContent)
	{
		using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
		archive.GetEntry(entryName)?.Delete();
		WriteEntry(archive, entryName, newContent);
	}

	/// <summary>Adds a brand-new entry to an existing archive, keeping every other entry unchanged.</summary>
	public static void AddEntry(string archivePath, string entryName, string content)
	{
		using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
		WriteEntry(archive, entryName, content);
	}

	public static string ReadEntryText(string archivePath, string entryName)
	{
		using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
		var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"No entry '{entryName}'.");
		using var reader = new StreamReader(entry.Open());
		return reader.ReadToEnd();
	}

	public static void WriteEntryText(string archivePath, string entryName, string content) =>
		ReplaceEntry(archivePath, entryName, content);

	private static void WriteEntry(ZipArchive archive, string entryName, string content)
	{
		var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
		using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		writer.Write(content);
	}

	public static string Sha256Hex(string content) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
