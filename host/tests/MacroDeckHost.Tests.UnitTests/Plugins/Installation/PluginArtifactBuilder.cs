using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

internal sealed class PluginArtifactBuilder
{
	private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _externalAttributes = new(StringComparer.Ordinal);

	private string _manifestJson = string.Empty;

	public static string Sha256Of(byte[] content)
	{
		return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
	}

	public PluginArtifactBuilder WithManifest(string manifestJson)
	{
		_manifestJson = manifestJson;
		return this;
	}

	public PluginArtifactBuilder WithFile(string path, string content)
	{
		_files[path] = Encoding.UTF8.GetBytes(content);
		return this;
	}

	public PluginArtifactBuilder WithFile(string path, byte[] content)
	{
		_files[path] = content;
		return this;
	}

	public PluginArtifactBuilder WithRawEntry(string path, string content, int unixMode)
	{
		_files[path] = Encoding.UTF8.GetBytes(content);
		_externalAttributes[path] = unixMode << 16;
		return this;
	}

	public string WriteTo(string directory, string fileName = "plugin.macroDeckPlugin")
	{
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, fileName);

		using var stream = File.Create(path);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

		if (_manifestJson.Length > 0)
		{
			WriteEntry(archive, "manifest.json", Encoding.UTF8.GetBytes(_manifestJson));
		}

		foreach (var (entryPath, content) in _files)
		{
			var entry = WriteEntry(archive, entryPath, content);
			if (_externalAttributes.TryGetValue(entryPath, out var attributes))
			{
				entry.ExternalAttributes = attributes;
			}
		}

		return path;
	}

	private static ZipArchiveEntry WriteEntry(ZipArchive archive, string path, byte[] content)
	{
		var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
		using var entryStream = entry.Open();
		entryStream.Write(content);
		return entry;
	}
}

internal static class ManifestJson
{
	public const string DefaultPluginId = "com.suchbyte.test-plugin";

	public const string EntrypointExecutable = "TestPlugin";

	public static string Minimal(string version = "1.0.0", string pluginId = DefaultPluginId)
	{
		return Build(version, pluginId, extraBlocks: null);
	}

	public static string Build(string version, string pluginId, string? extraBlocks)
	{
		var extras = extraBlocks is null ? string.Empty : "," + extraBlocks;

		return $$"""
				 {
				 	"manifestVersion": 1,
				 	"id": "{{pluginId}}",
				 	"name": "Test Plugin",
				 	"version": "{{version}}",
				 	"entrypoints": {
				 		"win-x64": { "executable": "{{EntrypointExecutable}}" },
				 		"linux-x64": { "executable": "{{EntrypointExecutable}}" },
				 		"linux-arm64": { "executable": "{{EntrypointExecutable}}" },
				 		"osx-x64": { "executable": "{{EntrypointExecutable}}" },
				 		"osx-arm64": { "executable": "{{EntrypointExecutable}}" }
				 	}{{extras}}
				 }
				 """;
	}
}
