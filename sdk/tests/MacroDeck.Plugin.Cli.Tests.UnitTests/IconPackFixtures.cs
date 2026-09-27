using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

internal static class IconPackFixtures
{
	public static void Write(string path,
		string name,
		IReadOnlyList<string> iconNames,
		JsonObject? ai = null,
		bool withAi = true,
		IReadOnlyCollection<string>? withoutMaster = null)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

		var icons = new JsonArray();
		var ids = new List<(Guid Id, string Name)>();

		foreach (var iconName in iconNames)
		{
			var id = Guid.NewGuid();
			ids.Add((id, iconName));
			icons.Add(new JsonObject { ["id"] = id.ToString(), ["name"] = iconName });
		}

		var manifest = new JsonObject { ["name"] = name, ["version"] = "1.0.0", ["icons"] = icons };

		if (withAi)
		{
			manifest["ai"] = ai ?? new JsonObject { ["generatedAssets"] = false };
		}

		using var stream = File.Create(path);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

		using (var writer = new StreamWriter(archive.CreateEntry("pack.json").Open()))
		{
			writer.Write(manifest.ToJsonString());
		}

		foreach (var (id, iconName) in ids)
		{
			if (withoutMaster?.Contains(iconName) == true)
			{
				continue;
			}

			using var entry = archive.CreateEntry($"icons/{id}/master.webp").Open();
			entry.Write("RIFF-webp-bytes"u8);
		}
	}

	public static JsonDocument ReadManifest(string projectDirectory)
		=> JsonDocument.Parse(File.ReadAllText(Path.Combine(projectDirectory, "manifest.json")));
}
