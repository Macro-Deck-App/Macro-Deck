using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

public sealed class PluginDependencyResolver : IPluginDependencyResolver
{
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginManifestReader _manifestReader;

	public PluginDependencyResolver(IPluginInstallationCatalog catalog, IPluginManifestReader manifestReader)
	{
		_catalog = catalog;
		_manifestReader = manifestReader;
	}

	public IReadOnlyList<PluginInstallWarning> Resolve(PluginManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);

		var warnings = new List<PluginInstallWarning>();
		var installed = ActiveVersionsById();

		foreach (var dependency in manifest.Dependencies ?? [])
		{
			ResolveDependency(dependency, installed, warnings);
		}

		foreach (var conflict in manifest.Conflicts ?? [])
		{
			ResolveConflict(conflict, installed, warnings);
		}

		foreach (var iconPack in manifest.IconPacks ?? [])
		{
			warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.IconPackUnresolved,
				$"Icon pack '{iconPack.Id}' cannot be resolved yet; icon pack dependencies are recorded " +
				"but not enforced.",
				iconPack.Id));
		}

		foreach (var permission in manifest.Permissions ?? [])
		{
			if (!PluginPermissions.IsKnown(permission))
			{
				warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.UnknownPermission,
					$"Permission '{permission}' is not one this version of Macro Deck knows about.",
					permission));
			}
		}

		return warnings;
	}

	public IReadOnlyList<string> FindHardDependents(string pluginId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

		var dependents = new List<string>();

		foreach (var installed in SafeDiscover())
		{
			if (installed.ActiveVersion is not { } active ||
				string.Equals(installed.PluginId, pluginId, StringComparison.Ordinal))
			{
				continue;
			}

			var read = _manifestReader.Read(active.ManifestPath, installed.PluginId, active.Version);
			if (!read.Success || read.Manifest is not { } manifest)
			{
				continue;
			}

			var dependsHard = (manifest.Dependencies ?? [])
				.Any(dependency => !dependency.Optional &&
					string.Equals(dependency.Id, pluginId, StringComparison.Ordinal));

			if (dependsHard)
			{
				dependents.Add(installed.PluginId);
			}
		}

		return dependents;
	}

	private static void ResolveDependency(PluginDependency dependency,
		Dictionary<string, string> installed,
		List<PluginInstallWarning> warnings)
	{
		var severity = dependency.Optional
			? PluginInstallWarningSeverity.Advisory
			: PluginInstallWarningSeverity.Blocking;

		if (!installed.TryGetValue(dependency.Id, out var activeVersion))
		{
			warnings.Add(Warn(severity,
				PluginDependencyWarningCodes.DependencyUnresolved,
				$"Required plugin '{dependency.Id}' is not installed.",
				dependency.Id));
			return;
		}

		if (!SatisfiesRange(activeVersion, dependency.VersionRange))
		{
			warnings.Add(Warn(severity,
				PluginDependencyWarningCodes.DependencyVersionUnsatisfied,
				$"Plugin '{dependency.Id}' is installed at {activeVersion}, which does not satisfy " +
				$"'{dependency.VersionRange}'.",
				dependency.Id));
		}
	}

	private static void ResolveConflict(PluginDependency conflict,
		Dictionary<string, string> installed,
		List<PluginInstallWarning> warnings)
	{
		if (!installed.TryGetValue(conflict.Id, out var activeVersion))
		{
			return;
		}

		if (!SatisfiesRange(activeVersion, conflict.VersionRange))
		{
			return;
		}

		warnings.Add(PluginInstallWarning.Blocking(PluginDependencyWarningCodes.ConflictInstalled,
			$"Plugin '{conflict.Id}' {activeVersion} is installed and conflicts with this plugin.",
			conflict.Id));
	}

	private static bool SatisfiesRange(string installedVersion, string? versionRange)
	{
		if (string.IsNullOrWhiteSpace(versionRange))
		{
			return true;
		}

		return SemanticVersionRange.TryParse(versionRange, out var range) &&
			SemanticVersion.TryParse(installedVersion, out var version) &&
			range.Satisfies(version);
	}

	private static PluginInstallWarning Warn(PluginInstallWarningSeverity severity,
		string code,
		string message,
		string subjectId)
	{
		return severity == PluginInstallWarningSeverity.Blocking
			? PluginInstallWarning.Blocking(code, message, subjectId)
			: PluginInstallWarning.Advisory(code, message, subjectId);
	}

	private Dictionary<string, string> ActiveVersionsById()
	{
		var installed = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var plugin in SafeDiscover())
		{
			if (plugin.ActiveVersion is { } active)
			{
				installed[plugin.PluginId] = active.Version;
			}
		}

		return installed;
	}

	private IReadOnlyList<InstalledPlugin> SafeDiscover()
	{
		try
		{
			return _catalog.Discover();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}
}
