using MacroDeck.LicenseTool.Collectors;
using MacroDeck.LicenseTool.Configuration;
using MacroDeck.LicenseTool.Licensing;
using MacroDeck.LicenseTool.Model;
using MacroDeck.LicenseTool.Npm;
using MacroDeck.LicenseTool.Output;

namespace MacroDeck.LicenseTool;

internal sealed record GenerationResult(string ThirdPartyNotices, string Notice, IReadOnlyCollection<string> Problems);

internal static class NoticeGenerator
{
	public const string LicenseTextDirectory = "third-party/licenses";

	public static GenerationResult Generate(string repositoryRoot, ICargoMetadataSource cargoMetadata)
	{
		var configuration = ConfigurationLoader.Load(repositoryRoot);
		var problems = new Problems();
		var components = new ComponentSet();

		var incomplete = new HashSet<Ecosystem>();
		foreach (var source in configuration.Tool.NuGet)
		{
			if (!NuGetCollector.Collect(repositoryRoot, source, components, problems))
			{
				incomplete.Add(Ecosystem.NuGet);
			}
		}

		if (configuration.Tool.Npm is { } npm && !NpmCollector.Collect(repositoryRoot, npm, components, problems))
		{
			incomplete.Add(Ecosystem.Npm);
		}

		foreach (var source in configuration.Tool.Cargo)
		{
			CargoCollector.Collect(repositoryRoot, source, cargoMetadata, components, problems);
		}

		var policy = new LicensePolicy(configuration.Tool.Policy.AllowedLicenses, configuration.Tool.Policy.AllowedExceptions);
		var notices = new List<(string Title, string Text)>();
		var reviewed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

		ApplyOverrides(repositoryRoot, configuration.Overrides, components, notices, reviewed, policy, incomplete, problems);
		var attributedFiles = AddAttributions(repositoryRoot, configuration.Attributions, components, notices, reviewed, problems);
		if (configuration.Tool.Assets is { } assets)
		{
			AssetPolicy.Check(repositoryRoot, assets, attributedFiles, problems);
		}

		foreach (var component in components.All)
		{
			SelectLicenses(component, policy, reviewed.GetValueOrDefault(component.Key) ?? [], problems);
			PruneTexts(component);
			AddFallbackTexts(repositoryRoot, component, problems);
		}

		foreach (var component in components.All.Where(component => component.SelectedLicenses.Any(license => license.Contains("Apache-2.0", StringComparison.Ordinal))))
		{
			notices.AddRange(component.Texts
				.Where(text => text.Kind == LicenseTextKind.Notice && text.Title.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase))
				.Select(text => (component.Name, text.Content)));
		}

		var allPlatforms = configuration.Tool.NuGet.SelectMany(source => source.RuntimeIdentifiers)
			.Concat(configuration.Tool.Cargo.SelectMany(source => source.Targets))
			.Select(target => target.Platform)
			.ToHashSet(StringComparer.Ordinal);

		return new GenerationResult(
			ThirdPartyNoticesWriter.Write(components.All, allPlatforms),
			NoticeWriter.Write(configuration.Tool.NoticeHeader, notices),
			problems.Messages);
	}

	private static void ApplyOverrides(
		string repositoryRoot,
		OverridesFile overrides,
		ComponentSet components,
		List<(string Title, string Text)> notices,
		Dictionary<string, HashSet<string>> reviewed,
		LicensePolicy policy,
		HashSet<Ecosystem> incomplete,
		Problems problems)
	{
		foreach (var entry in overrides.Packages)
		{
			if (!Enum.TryParse<Ecosystem>(entry.Ecosystem, ignoreCase: true, out var ecosystem) || ecosystem == Ecosystem.Asset)
			{
				problems.Add($"overrides.yml: {entry.Name}: unknown ecosystem '{entry.Ecosystem}' (nuget, npm or cargo)");
				continue;
			}

			var component = components.Find(ecosystem, entry.Name);
			if (component is null)
			{
				if (!incomplete.Contains(ecosystem))
				{
					problems.Add($"overrides.yml: {entry.Ecosystem} {entry.Name} matches no shipped component; remove the stale override");
				}

				continue;
			}

			if (entry.Exclude is not null)
			{
				components.Remove(component);
				continue;
			}

			if (entry.License is not null)
			{
				component.DeclaredLicenses.Clear();
				component.DeclaredLicenses.Add(entry.License);
			}

			if (entry.Reviewed is not null && entry.License is null)
			{
				problems.Add($"overrides.yml: {entry.Name}: reviewed needs the reviewed license in license");
			}
			else if (entry.Reviewed is not null)
			{
				reviewed[component.Key] = LicenseIds(entry.License!, problems, $"overrides.yml: {entry.Name}");
			}

			if (entry.LicenseFile is not null)
			{
				component.Texts.RemoveAll(text => text.Kind == LicenseTextKind.License);
				AddRepositoryText(repositoryRoot, component, entry.LicenseFile, Path.GetFileName(entry.LicenseFile), problems);
			}

			component.Url = entry.Url ?? component.Url;
			component.Copyright = entry.Copyright ?? component.Copyright;
			if (entry.Notice is not null)
			{
				notices.Add((component.Name, entry.Notice));
			}

			foreach (var additional in entry.AdditionalLicenses)
			{
				var selected = Select(policy, additional.License, [], problems, $"overrides.yml: {entry.Name}: {additional.Title}");
				if (selected is null)
				{
					continue;
				}

				var content = additional.LicenseFile is not null
					? ReadRepositoryFile(repositoryRoot, additional.LicenseFile, problems)
					: StandardText(repositoryRoot, selected, additional.Copyright, null, problems);
				if (content is not null)
				{
					var header = additional.Copyright is null ? "" : $"Copyright: {additional.Copyright}\n\n";
					component.AddText(new LicenseText($"{additional.Title} ({string.Join(" AND ", selected)})",
						$"{additional.Title}\n\n{header}{content}", LicenseTextKind.License, IsAdditional: true));
				}
			}
		}
	}

	private static HashSet<string> AddAttributions(
		string repositoryRoot,
		AttributionsFile attributions,
		ComponentSet components,
		List<(string Title, string Text)> notices,
		Dictionary<string, HashSet<string>> reviewed,
		Problems problems)
	{
		var owners = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var asset in attributions.Assets)
		{
			foreach (var pattern in asset.Paths)
			{
				var matches = Glob.Expand(repositoryRoot, pattern);
				if (matches.Count == 0)
				{
					problems.Add($"attributions.yml: {asset.Name}: path '{pattern}' matches no file; update or remove the attribution");
				}

				foreach (var match in matches)
				{
					if (owners.TryGetValue(match, out var owner) && owner != asset.Name)
					{
						problems.Add($"attributions.yml: {match} is claimed by both '{owner}' and '{asset.Name}'");
					}

					owners.TryAdd(match, asset.Name);
				}
			}

			var component = new Component(Ecosystem.Asset, asset.Name)
			{
				Url = asset.Url,
				Copyright = asset.Copyright,
				Note = asset.Note,
			};
			component.DeclaredLicenses.Add(asset.License);
			if (asset.Reviewed is not null)
			{
				reviewed[component.Key] = LicenseIds(asset.License, problems, $"attributions.yml: {asset.Name}");
			}

			if (asset.LicenseFile is not null)
			{
				AddRepositoryText(repositoryRoot, component, asset.LicenseFile, Path.GetFileName(asset.LicenseFile), problems);
			}

			if (asset.Notice is not null)
			{
				notices.Add((asset.Name, asset.Notice));
			}

			components.Add(component);
		}

		return owners.Keys.ToHashSet(StringComparer.Ordinal);
	}

	private static void SelectLicenses(Component component, LicensePolicy policy, HashSet<string> reviewed, Problems problems)
	{
		var label = $"{component.Ecosystem.ToString().ToLowerInvariant()} {component.Name}";
		if (component.DeclaredLicenses.Count == 0)
		{
			problems.Add($"{label}: no license metadata; add an override with license (and licenseFile) to third-party/overrides.yml");
			return;
		}

		foreach (var declared in component.DeclaredLicenses)
		{
			var selected = Select(policy, declared, reviewed, problems, label);
			if (selected is not null)
			{
				component.SelectedLicenses.Add(string.Join(" AND ", selected));
			}
		}
	}

	private static IReadOnlyList<SelectedLicense>? Select(
		LicensePolicy policy,
		string expression,
		IReadOnlyCollection<string> reviewed,
		Problems problems,
		string label)
	{
		try
		{
			var selected = policy.Select(expression, reviewed);
			if (selected is null)
			{
				problems.Add($"{label}: license '{expression}' is not allowed by third-party/config.yml; review it and add an override with reviewed: to third-party/overrides.yml, or replace the dependency");
			}

			return selected;
		}
		catch (FormatException exception)
		{
			problems.Add($"{label}: {exception.Message}; add an override with an SPDX license to third-party/overrides.yml");
			return null;
		}
	}

	private static HashSet<string> LicenseIds(string expression, Problems problems, string label)
	{
		try
		{
			return Collect(SpdxExpression.Parse(expression)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		}
		catch (FormatException exception)
		{
			problems.Add($"{label}: {exception.Message}");
			return [];
		}

		static IEnumerable<string> Collect(SpdxExpression expression) => expression switch
		{
			SpdxLicense license => [license.Id],
			SpdxAnd and => and.Operands.SelectMany(Collect),
			SpdxOr or => or.Operands.SelectMany(Collect),
			_ => [],
		};
	}

	private static void PruneTexts(Component component)
	{
		var selectedIds = component.SelectedLicenses
			.SelectMany(selected => selected.Split(" AND ", StringSplitOptions.None))
			.Select(selected => selected.Split(" WITH ", StringSplitOptions.None)[0])
			.ToList();
		if (selectedIds.Count == 0)
		{
			return;
		}

		component.Texts.RemoveAll(text => text.Kind == LicenseTextKind.License
			&& !text.IsAdditional
			&& LicenseFiles.NamesUnselectedLicense(text.Title, selectedIds));
	}

	private static void AddFallbackTexts(string repositoryRoot, Component component, Problems problems)
	{
		if (component.Texts.Any(text => text.Kind == LicenseTextKind.License && !text.IsAdditional))
		{
			return;
		}

		foreach (var selected in component.SelectedLicenses)
		{
			var ids = selected.Split(" AND ", StringSplitOptions.None)
				.Select(part =>
				{
					var pieces = part.Split(" WITH ", StringSplitOptions.None);
					return new SelectedLicense(pieces[0], pieces.Length > 1 ? pieces[1] : null);
				})
				.ToList();
			var content = StandardText(repositoryRoot, ids, component.Copyright, component.Url, problems);
			if (content is not null)
			{
				component.AddText(new LicenseText(selected, content, LicenseTextKind.License));
			}
		}
	}

	private static string? StandardText(
		string repositoryRoot,
		IReadOnlyList<SelectedLicense> licenses,
		string? copyright,
		string? url,
		Problems problems)
	{
		var holders = !string.IsNullOrWhiteSpace(copyright)
			? $"Copyright holders: {copyright.Trim()}"
			: $"Copyright holders: not stated in package metadata; see {(string.IsNullOrEmpty(url) ? "the package" : url)}";
		var parts = new List<string> { holders };
		foreach (var license in licenses)
		{
			foreach (var id in license.Exception is null ? [license.Id] : new[] { license.Id, license.Exception })
			{
				var text = ReadRepositoryFile(repositoryRoot, $"{LicenseTextDirectory}/{id}.txt", problems);
				if (text is null)
				{
					return null;
				}

				parts.Add(text);
			}
		}

		return string.Join("\n\n", parts);
	}

	private static void AddRepositoryText(string repositoryRoot, Component component, string relativePath, string title, Problems problems)
	{
		if (ReadRepositoryFile(repositoryRoot, relativePath, problems) is { } content)
		{
			component.AddText(new LicenseText(title, content, LicenseFiles.Classify(title) ?? LicenseTextKind.License));
		}
	}

	private static string? ReadRepositoryFile(string repositoryRoot, string relativePath, Problems problems)
	{
		var path = Path.Combine(repositoryRoot, relativePath);
		if (File.Exists(path))
		{
			return File.ReadAllText(path);
		}

		problems.Add($"missing license text {relativePath}");
		return null;
	}
}
