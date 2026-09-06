using System.Text.Json;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// Resolves the <c>"&lt;pack name&gt;.&lt;icon id&gt;"</c> strings a Macro Deck 2 button stores. The pack half is
/// the pack's <b>display name</b> from its manifest, not its package id or its folder, and the split is at
/// the first dot - both exactly as Macro Deck 2's own <c>IconManager.GetIconByString</c> does, so a pack
/// whose name contains a dot resolves here the same way it did there.
/// </summary>
internal sealed class MacroDeck2IconCatalog
{
	private static readonly string[] _imageExtensions = [".png", ".gif", ".jpeg", ".jpg", ".webp"];

	private readonly Dictionary<string, MacroDeck2IconPack> _byName = new(StringComparer.OrdinalIgnoreCase);

	private MacroDeck2IconCatalog(Dictionary<string, MacroDeck2IconPack> byName)
	{
		_byName = byName;
	}

	public static MacroDeck2IconCatalog Load(string iconPacksDirectory)
	{
		var byName = new Dictionary<string, MacroDeck2IconPack>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(iconPacksDirectory))
		{
			return new MacroDeck2IconCatalog(byName);
		}

		foreach (var directory in Directory.EnumerateDirectories(iconPacksDirectory))
		{
			var manifestPath = Path.Combine(directory, "ExtensionManifest.json");
			if (!File.Exists(manifestPath))
			{
				continue;
			}

			string? name;
			try
			{
				using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
				name = document.RootElement.TryGetProperty("name", out var nameElement) &&
					nameElement.ValueKind == JsonValueKind.String
						? nameElement.GetString()
						: null;
			}
			catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			// A duplicate display name is possible; the first one wins, which is what Macro Deck 2's own
			// first-match lookup did too.
			byName.TryAdd(name, new MacroDeck2IconPack(name, directory));
		}

		return new MacroDeck2IconCatalog(byName);
	}

	public static bool IsImage(string path)
		=> _imageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

	/// <summary>The pack a button's icon string names, or null when this installation does not hold it.</summary>
	public MacroDeck2IconPack? ResolvePack(string? reference)
	{
		if (string.IsNullOrWhiteSpace(reference))
		{
			return null;
		}

		var separator = reference.IndexOf('.', StringComparison.Ordinal);
		return separator <= 0 || separator == reference.Length - 1
			? null
			: _byName.GetValueOrDefault(reference[..separator]);
	}

	/// <summary>The image file a button's icon string points at, or null when nothing resolves it.</summary>
	public MacroDeck2Icon? Resolve(string? reference)
	{
		if (string.IsNullOrWhiteSpace(reference))
		{
			return null;
		}

		var separator = reference.IndexOf('.', StringComparison.Ordinal);
		if (separator <= 0 || separator == reference.Length - 1)
		{
			return null;
		}

		var packName = reference[..separator];
		var iconId = reference[(separator + 1)..];
		if (!_byName.TryGetValue(packName, out var pack))
		{
			return null;
		}

		foreach (var extension in _imageExtensions)
		{
			var candidate = Path.Combine(pack.Directory, iconId + extension);
			if (File.Exists(candidate))
			{
				return new MacroDeck2Icon(pack.Name, iconId, candidate);
			}
		}

		return null;
	}
}

internal sealed record MacroDeck2IconPack(string Name, string Directory)
{
	/// <summary>Every image the pack holds, so a referenced pack migrates whole rather than reduced to the
	/// icons that happened to be on a button.</summary>
	public IReadOnlyList<MacroDeck2Icon> Images()
	{
		try
		{
			return System.IO.Directory.EnumerateFiles(Directory)
				.Where(file => MacroDeck2IconCatalog.IsImage(file) &&
					!Path.GetFileName(file).Equals("ExtensionIcon.png", StringComparison.OrdinalIgnoreCase))
				.Select(file => new MacroDeck2Icon(Name, Path.GetFileNameWithoutExtension(file), file))
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}
}

internal sealed record MacroDeck2Icon(string PackName, string IconId, string FilePath);
