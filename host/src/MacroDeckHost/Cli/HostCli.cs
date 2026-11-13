using MacroDeck.Localization;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Localization;
using Serilog.Core;

namespace MacroDeckHost.Cli;

public sealed class HostCli
{
	public const int Success = 0;

	public const int Failure = 1;

	private const string QuitOption = "--quit";

	private readonly TextWriter _output;
	private readonly string? _culture;
	private readonly LocalizationResolver _resolver;

	public HostCli(TextWriter output, string? culture)
	{
		_output = output;
		_culture = culture;

		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(MacroDeckStrings.LocalizationCatalog);
		catalogs.Register(AppStrings.LocalizationCatalog);
		_resolver = new LocalizationResolver(catalogs);
	}

	public int Run(HostCommand command, DataRootEnvironment environment)
	{
		return command switch
		{
			HostCommand.Help => Help(),
			HostCommand.Version => Version(),
			HostCommand.ListPlugins => ListPlugins(environment),
			_ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
		};
	}

	private int Help()
	{
		Write(AppStrings.Cli.Help.Title());
		_output.WriteLine();
		WriteOption(HostCommandLine.HelpOption + ", " + HostCommandLine.ShortHelpOption,
			AppStrings.Cli.Help.ShowHelp());
		WriteOption(HostCommandLine.VersionOption, AppStrings.Cli.Help.Version());
		WriteOption(HostCommandLine.ListPluginsOption, AppStrings.Cli.Help.ListPlugins());
		WriteOption(HostCommandLine.DisablePluginsOption, AppStrings.Cli.Help.DisablePlugins());
		WriteOption(QuitOption, AppStrings.Cli.Help.Quit());
		return Success;
	}

	private int Version()
	{
		Write(AppStrings.Update.Details.VersionHeading(HostVersion.Current));
		Write(AppStrings.Cli.Version.Channel(BuildConfig.Channel.ToString()));
		if (HostBuildInfo.Commit is { } commit)
		{
			Write(AppStrings.Cli.Version.Commit(commit));
		}

		return Success;
	}

	private int ListPlugins(DataRootEnvironment environment)
	{
		var paths = MacroDeckPaths.WithoutCreatingDirectories(environment);
		var pluginsDirectory = paths.PluginsDirectory;
		Write(AppStrings.Cli.ListPlugins.Directory(pluginsDirectory));

		string[] pluginDirectories;
		try
		{
			pluginDirectories = Directory.Exists(pluginsDirectory)
				? Directory.GetDirectories(pluginsDirectory)
				: [];
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Write(AppStrings.Cli.ListPlugins.DirectoryUnreadable(e.Message));
			return Failure;
		}

		var catalog = new PluginInstallationCatalog(paths, Logger.None);
		var reader = new PluginManifestReader();
		var listed = 0;

		foreach (var pluginDirectory in pluginDirectories.Order(StringComparer.Ordinal))
		{
			var folderName = Path.GetFileName(pluginDirectory);
			if (folderName.StartsWith('.')
				|| folderName == Path.GetFileName(paths.PluginStagingDirectory)
				|| folderName == Path.GetFileName(paths.PluginCacheDirectory))
			{
				continue;
			}

			_output.WriteLine();
			listed++;
			if (ReadPlugin(catalog, pluginDirectory) is { } plugin)
			{
				WritePlugin(plugin, reader);
			}
			else
			{
				Write(AppStrings.Cli.ListPlugins.UnreadableFolder(folderName));
			}
		}

		if (listed == 0)
		{
			Write(AppStrings.Cli.ListPlugins.None());
		}

		return Success;
	}

	private static InstalledPlugin? ReadPlugin(PluginInstallationCatalog catalog, string pluginDirectory)
	{
		try
		{
			return catalog.ReadPlugin(pluginDirectory);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void WritePlugin(InstalledPlugin plugin, PluginManifestReader reader)
	{
		_output.WriteLine(plugin.PluginId);

		if (plugin.ActiveVersion is { } active)
		{
			var manifest = reader.Read(active.ManifestPath, plugin.PluginId, active.Version);
			if (manifest.Manifest is { } valid)
			{
				WriteDetail(AppStrings.Cli.ListPlugins.Name(valid.Name));
			}

			WriteDetail(AppStrings.Cli.ListPlugins.ActiveVersion(active.Version));
			if (manifest.Error is { } error)
			{
				WriteDetail(AppStrings.Cli.ListPlugins.ManifestInvalid(active.Version, error.ToString()));
			}
		}
		else
		{
			WriteDetail(AppStrings.Cli.ListPlugins.NoActiveVersion());
		}

		var others = plugin.Versions
			.Select(version => version.Version)
			.Where(version => version != plugin.ActiveVersion?.Version)
			.Order(StringComparer.Ordinal)
			.ToList();
		if (others.Count > 0)
		{
			WriteDetail(AppStrings.Cli.ListPlugins.OtherVersions(string.Join(", ", others)));
		}

		foreach (var version in MissingManifestVersions(plugin))
		{
			WriteDetail(AppStrings.Cli.ListPlugins.ManifestMissing(version));
		}
	}

	private static List<string> MissingManifestVersions(InstalledPlugin plugin)
	{
		var versionsDirectory = Path.Combine(plugin.PluginDirectory, "versions");
		string[] directories;
		try
		{
			directories = Directory.Exists(versionsDirectory) ? Directory.GetDirectories(versionsDirectory) : [];
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return [];
		}

		var known = plugin.Versions.Select(version => version.Version).ToHashSet(StringComparer.Ordinal);
		return directories
			.Select(Path.GetFileName)
			.OfType<string>()
			.Where(version => !known.Contains(version))
			.Order(StringComparer.Ordinal)
			.ToList();
	}

	private void WriteOption(string option, LocalizedString description)
	{
		_output.WriteLine("  " + option);
		_output.WriteLine("      " + Resolve(description));
	}

	private void WriteDetail(LocalizedString text)
	{
		_output.WriteLine("  " + Resolve(text));
	}

	private void Write(LocalizedString text)
	{
		_output.WriteLine(Resolve(text));
	}

	private string Resolve(LocalizedString text)
	{
		return _resolver.Resolve(text, _culture);
	}
}
