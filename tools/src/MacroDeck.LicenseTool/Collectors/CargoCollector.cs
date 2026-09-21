using System.Diagnostics;
using System.Text.Json;
using MacroDeck.LicenseTool.Configuration;
using MacroDeck.LicenseTool.Model;

namespace MacroDeck.LicenseTool.Collectors;

internal interface ICargoMetadataSource
{
	string Read(string manifestDirectory, string targetTriple);
}

internal sealed class CargoProcessMetadataSource : ICargoMetadataSource
{
	public string Read(string manifestDirectory, string targetTriple)
	{
		var startInfo = new ProcessStartInfo("cargo")
		{
			WorkingDirectory = manifestDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		foreach (var argument in new[] { "metadata", "--format-version", "1", "--locked", "--filter-platform", targetTriple })
		{
			startInfo.ArgumentList.Add(argument);
		}

		Process process;
		try
		{
			process = Process.Start(startInfo) ?? throw new ToolException("cargo could not be started");
		}
		catch (System.ComponentModel.Win32Exception)
		{
			throw new ToolException("cargo was not found on PATH; install the Rust toolchain to collect crate licenses");
		}

		using (process)
		{
			var errorTask = process.StandardError.ReadToEndAsync();
			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				throw new ToolException($"cargo metadata failed in {manifestDirectory}: {errorTask.Result.Trim()}");
			}

			return output;
		}
	}
}

internal static class CargoCollector
{
	public static void Collect(
		string repositoryRoot,
		CargoSourceConfiguration source,
		ICargoMetadataSource metadataSource,
		ComponentSet components,
		Problems problems)
	{
		var manifestDirectory = Path.Combine(repositoryRoot, source.ManifestDirectory);
		foreach (var target in source.Targets)
		{
			using var document = JsonDocument.Parse(metadataSource.Read(manifestDirectory, target.Id));
			CollectTarget(document.RootElement, target.Platform, components, problems);
		}
	}

	private static void CollectTarget(JsonElement metadata, string platform, ComponentSet components, Problems problems)
	{
		var packages = metadata.GetProperty("packages").EnumerateArray()
			.ToDictionary(package => package.GetProperty("id").GetString()!, package => package);
		var resolve = metadata.GetProperty("resolve");
		var nodes = resolve.GetProperty("nodes").EnumerateArray()
			.ToDictionary(node => node.GetProperty("id").GetString()!, node => node);
		var roots = resolve.GetProperty("root").ValueKind == JsonValueKind.String
			? [resolve.GetProperty("root").GetString()!]
			: metadata.GetProperty("workspace_members").EnumerateArray().Select(member => member.GetString()!).ToList();

		var visited = new HashSet<string>(roots, StringComparer.Ordinal);
		var pending = new Stack<string>(roots);
		while (pending.Count > 0)
		{
			foreach (var dependency in nodes[pending.Pop()].GetProperty("deps").EnumerateArray())
			{
				var id = dependency.GetProperty("pkg").GetString()!;
				var normal = dependency.GetProperty("dep_kinds").EnumerateArray()
					.Any(kind => kind.GetProperty("kind").ValueKind == JsonValueKind.Null);
				if (!normal || visited.Contains(id) || IsProcMacro(packages[id]))
				{
					continue;
				}

				visited.Add(id);
				pending.Push(id);
				components.Add(ToComponent(packages[id], platform, problems));
			}
		}
	}

	private static bool IsProcMacro(JsonElement package) =>
		package.GetProperty("targets").EnumerateArray().Any(target =>
			target.GetProperty("kind").EnumerateArray().Any(kind => kind.GetString() == "proc-macro"));

	private static Component ToComponent(JsonElement package, string platform, Problems problems)
	{
		var name = package.GetProperty("name").GetString()!;
		var component = new Component(Ecosystem.Cargo, name);
		component.Versions.Add(package.GetProperty("version").GetString()!);
		component.Platforms.Add(platform);
		if (String(package, "license") is { } license)
		{
			component.DeclaredLicenses.Add(license);
		}

		component.Url = TextNormalizer.CleanUrl(String(package, "repository") ?? String(package, "homepage"));
		if (package.TryGetProperty("authors", out var authors) && authors.GetArrayLength() > 0)
		{
			component.Copyright = string.Join(", ", authors.EnumerateArray().Select(author => author.GetString()));
		}

		var directory = Path.GetDirectoryName(package.GetProperty("manifest_path").GetString()!)!;
		if (!Directory.Exists(directory))
		{
			problems.Add($"cargo: sources of {name} are missing at {directory}; run cargo fetch");
			return component;
		}

		var licenseFile = String(package, "license_file");
		foreach (var file in Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal))
		{
			if (LicenseFiles.Classify(file!) is { } kind)
			{
				component.AddText(new LicenseText(file!, File.ReadAllText(Path.Combine(directory, file!)), kind));
			}
		}

		if (licenseFile is not null)
		{
			var path = Path.Combine(directory, licenseFile);
			if (File.Exists(path))
			{
				component.AddText(new LicenseText(Path.GetFileName(licenseFile), File.ReadAllText(path), LicenseTextKind.License));
			}
		}

		return component;
	}

	private static string? String(JsonElement element, string property) =>
		element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
