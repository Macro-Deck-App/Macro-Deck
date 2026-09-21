using System.Text.Json;
using System.Xml.Linq;
using MacroDeck.LicenseTool.Configuration;
using MacroDeck.LicenseTool.Model;

namespace MacroDeck.LicenseTool.Collectors;

internal static class NuGetCollector
{
	private static readonly string[] ShippedAssetGroups = ["runtime", "native", "resource"];

	public static bool Collect(string repositoryRoot, NuGetSourceConfiguration source, ComponentSet components, Problems problems)
	{
		var projectPath = Path.Combine(repositoryRoot, source.Project);
		var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
		var restoreCommand =
			$"dotnet restore {source.Project} '-p:RuntimeIdentifiers=\"{string.Join(';', source.RuntimeIdentifiers.Select(rid => rid.Id))}\"'";
		if (!File.Exists(assetsPath))
		{
			problems.Add($"nuget: {assetsPath} is missing; run: {restoreCommand}");
			return false;
		}

		using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
		var root = document.RootElement;
		var targets = root.GetProperty("targets");
		var libraries = root.GetProperty("libraries");
		var packageFolders = root.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();
		var complete = true;

		foreach (var runtime in source.RuntimeIdentifiers)
		{
			var targetName = $"{source.TargetFramework}/{runtime.Id}";
			if (!targets.TryGetProperty(targetName, out var target))
			{
				problems.Add($"nuget: {source.Project} was restored without the {runtime.Id} graph; run: {restoreCommand}");
				complete = false;
				continue;
			}

			foreach (var library in target.EnumerateObject())
			{
				if (library.Value.GetProperty("type").GetString() != "package" || !IsShipped(library.Value))
				{
					continue;
				}

				var separator = library.Name.IndexOf('/', StringComparison.Ordinal);
				var name = library.Name[..separator];
				var component = new Component(Ecosystem.NuGet, name);
				component.Versions.Add(library.Name[(separator + 1)..]);
				component.Platforms.Add(runtime.Platform);
				ReadPackage(libraries.GetProperty(library.Name), packageFolders, component, problems);
				components.Add(component);
			}
		}

		return complete;
	}

	private static bool IsShipped(JsonElement library) =>
		ShippedAssetGroups.Any(group =>
			library.TryGetProperty(group, out var assets)
			&& assets.EnumerateObject().Any(asset => !asset.Name.EndsWith("/_._", StringComparison.Ordinal)
				&& asset.Name != "_._"));

	private static void ReadPackage(JsonElement library, List<string> packageFolders, Component component, Problems problems)
	{
		var relative = library.GetProperty("path").GetString()!;
		var directory = packageFolders
			.Select(folder => Path.Combine(folder, relative))
			.FirstOrDefault(Directory.Exists);
		if (directory is null)
		{
			problems.Add($"nuget: package folder for {component.Name} not found; run dotnet restore");
			return;
		}

		var files = library.GetProperty("files").EnumerateArray().Select(file => file.GetString()!).ToList();
		var nuspecFile = files.FirstOrDefault(file => file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
			&& !file.Contains('/', StringComparison.Ordinal));
		string? licenseFile = null;
		if (nuspecFile is not null)
		{
			var metadata = XDocument.Load(Path.Combine(directory, nuspecFile)).Root?
				.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata");
			var license = Child(metadata, "license");
			if (license is not null && (string?)license.Attribute("type") == "expression")
			{
				component.DeclaredLicenses.Add(license.Value.Trim());
			}
			else if (license is not null && (string?)license.Attribute("type") == "file")
			{
				licenseFile = license.Value.Trim().Replace('\\', '/');
			}

			component.Url = TextNormalizer.CleanUrl(
				Child(metadata, "projectUrl")?.Value
				?? (string?)Child(metadata, "repository")?.Attribute("url"));
			var copyright = Child(metadata, "copyright")?.Value ?? Child(metadata, "authors")?.Value;
			component.Copyright = string.IsNullOrWhiteSpace(copyright) ? null : copyright.Trim();
		}

		foreach (var file in files.Where(file => !file.Contains('/', StringComparison.Ordinal)).Order(StringComparer.Ordinal))
		{
			var kind = string.Equals(file, licenseFile, StringComparison.OrdinalIgnoreCase)
				? LicenseTextKind.License
				: LicenseFiles.Classify(file);
			if (kind is { } textKind)
			{
				component.AddText(new LicenseText(file, File.ReadAllText(Path.Combine(directory, file)), textKind));
			}
		}

		if (licenseFile is not null && licenseFile.Contains('/', StringComparison.Ordinal)
			&& File.Exists(Path.Combine(directory, licenseFile)))
		{
			component.AddText(new LicenseText(Path.GetFileName(licenseFile),
				File.ReadAllText(Path.Combine(directory, licenseFile)), LicenseTextKind.License));
		}
	}

	private static XElement? Child(XElement? parent, string name) =>
		parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);
}
