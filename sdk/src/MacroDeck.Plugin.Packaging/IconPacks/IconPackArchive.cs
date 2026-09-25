using System.IO.Compression;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.IconPacks;

internal sealed record IconPackArchiveIcon
{
	public required Guid Id { get; init; }

	public required string Name { get; init; }

	public required bool HasMaster { get; init; }
}

internal sealed record IconPackArchiveInfo
{
	public string? Name { get; init; }

	public string? Version { get; init; }

	public PackageAiDeclaration? Ai { get; init; }

	public required IReadOnlyList<IconPackArchiveIcon> Icons { get; init; }

	public required IReadOnlyList<string> DuplicateNames { get; init; }

	public required IReadOnlyList<string> UnusableNames { get; init; }

	public bool NamesAreValid => DuplicateNames.Count == 0 && UnusableNames.Count == 0;
}

internal sealed record IconPackArchiveReadResult
{
	public IconPackArchiveInfo? Info { get; init; }

	public string? Error { get; init; }

	public bool Success => Info is not null;
}

internal static class IconPackArchive
{
	public const int MaxEntries = 10_000;

	public const int MaxIconNameLength = 128;

	public const int MaxManifestBytes = 16 * 1024 * 1024;

	public const string ManifestEntryName = "pack.json";

	public static readonly StringComparer IconNameComparer = StringComparer.OrdinalIgnoreCase;

	public static bool IsUsableIconName(string? name)
		=> !string.IsNullOrWhiteSpace(name) &&
			name.Length <= MaxIconNameLength &&
			name.Trim().Length == name.Length &&
			!name.Contains('/') &&
			!name.Any(char.IsControl);

	public static IconPackArchiveReadResult Read(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			return Read(stream);
		}
		catch (IOException ex)
		{
			return Fail($"The file cannot be read: {ex.Message}");
		}
		catch (UnauthorizedAccessException ex)
		{
			return Fail($"The file cannot be read: {ex.Message}");
		}
	}

	public static IconPackArchiveReadResult Read(Stream stream)
	{
		try
		{
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
			return Read(archive);
		}
		catch (InvalidDataException)
		{
			return Fail("The file is not a valid .macroDeckIconPack archive.");
		}
	}

	private static IconPackArchiveReadResult Read(ZipArchive archive)
	{
		if (archive.Entries.Count > MaxEntries)
		{
			return Fail($"The archive contains more than {MaxEntries} entries.");
		}

		var manifestEntry = archive.GetEntry(ManifestEntryName);
		if (manifestEntry is null)
		{
			return Fail("The archive contains no pack.json manifest.");
		}

		if (manifestEntry.Length > MaxManifestBytes)
		{
			return Fail($"The pack.json manifest is larger than {MaxManifestBytes} bytes.");
		}

		JsonElement root;
		try
		{
			using var manifestStream = manifestEntry.Open();
			using var document = JsonDocument.Parse(manifestStream);
			root = document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return Fail("The pack.json manifest is not parseable.");
		}

		if (root.ValueKind != JsonValueKind.Object)
		{
			return Fail("The pack.json manifest is not an object.");
		}

		var masters = IndexMasters(archive);
		var icons = new List<IconPackArchiveIcon>();
		if (TryGetProperty(root, "icons", out var iconsElement) && iconsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in iconsElement.EnumerateArray())
			{
				if (element.ValueKind != JsonValueKind.Object ||
					!TryGetProperty(element, "id", out var idElement) ||
					idElement.ValueKind != JsonValueKind.String ||
					!Guid.TryParse(idElement.GetString(), out var id))
				{
					continue;
				}

				var name = TryGetProperty(element, "name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
					? nameElement.GetString() ?? string.Empty
					: string.Empty;

				icons.Add(new IconPackArchiveIcon { Id = id, Name = name, HasMaster = masters.Contains(id) });
			}
		}

		var duplicates = icons
			.Where(icon => IsUsableIconName(icon.Name))
			.GroupBy(icon => icon.Name, IconNameComparer)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.Order(StringComparer.Ordinal)
			.ToList();

		var unusable = icons
			.Where(icon => !IsUsableIconName(icon.Name))
			.Select(icon => icon.Name)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		return new IconPackArchiveReadResult
		{
			Info = new IconPackArchiveInfo
			{
				Name = ReadString(root, "name"),
				Version = ReadString(root, "version"),
				Ai = ReadAi(root),
				Icons = icons,
				DuplicateNames = duplicates,
				UnusableNames = unusable
			}
		};
	}

	private static HashSet<Guid> IndexMasters(ZipArchive archive)
	{
		var masters = new HashSet<Guid>();
		foreach (var entry in archive.Entries)
		{
			var segments = entry.FullName.Replace('\\', '/').Split('/');
			if (segments.Length == 3 &&
				segments[0].Equals("icons", StringComparison.OrdinalIgnoreCase) &&
				segments[2].Equals("master.webp", StringComparison.OrdinalIgnoreCase) &&
				Guid.TryParse(segments[1], out var iconId))
			{
				masters.Add(iconId);
			}
		}

		return masters;
	}

	private static PackageAiDeclaration? ReadAi(JsonElement root)
	{
		if (!TryGetProperty(root, "ai", out var element) || element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		try
		{
			return element.Deserialize<PackageAiDeclaration>(PluginManifestJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string? ReadString(JsonElement root, string name)
		=> TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

	private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static IconPackArchiveReadResult Fail(string error) => new() { Error = error };
}
