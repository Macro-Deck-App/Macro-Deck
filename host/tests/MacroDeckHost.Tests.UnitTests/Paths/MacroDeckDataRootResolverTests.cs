using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Paths;

namespace MacroDeckHost.Tests.UnitTests.Paths;

public class MacroDeckDataRootResolverTests
{
	private static readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "macro-deck-app");
	private static readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "macro-deck-workspace");
	private static readonly string _homeDirectory = Path.Combine(Path.GetTempPath(), "macro-deck-home");

	private static DataRootEnvironment CreateEnvironment(
		DataRootPlatform platform,
		bool packagedMarkerExists = false,
		Dictionary<string, string>? environmentVariables = null)
	{
		var variables = environmentVariables ?? new Dictionary<string, string>();
		var markerPath = Path.Combine(_baseDirectory, MacroDeckDataRootResolver.PackagedMarkerFileName);
		return new DataRootEnvironment(_baseDirectory,
			_workingDirectory,
			_homeDirectory,
			platform,
			key => variables.GetValueOrDefault(key),
			path => packagedMarkerExists && path == markerPath);
	}

	private static string ChannelDefault(string productionDataRoot)
	{
		return BuildConfig.Channel == BuildChannel.Development
			? Path.Combine(_workingDirectory, ".data")
			: productionDataRoot;
	}

	[Test]
	public void Resolve_uses_data_dir_override_when_set()
	{
		var overrideDir = Path.Combine(Path.GetTempPath(), "custom-data-root");
		var environment = CreateEnvironment(DataRootPlatform.Linux,
			packagedMarkerExists: true,
			environmentVariables: new Dictionary<string, string>
			{
				[MacroDeckDataRootResolver.DataDirEnvironmentVariable] = overrideDir
			});

		Assert.That(MacroDeckDataRootResolver.Resolve(environment), Is.EqualTo(Path.GetFullPath(overrideDir)));
	}

	[Test]
	public void Resolve_uses_base_directory_when_portable_flag_is_set()
	{
		var environment = CreateEnvironment(DataRootPlatform.Windows,
			packagedMarkerExists: true,
			environmentVariables: new Dictionary<string, string>
			{
				[MacroDeckDataRootResolver.PortableEnvironmentVariable] = "1"
			});

		Assert.That(MacroDeckDataRootResolver.Resolve(environment), Is.EqualTo(_baseDirectory));
	}

	[Test]
	public void Resolve_uses_channel_default_without_an_override()
	{
		var environment = CreateEnvironment(DataRootPlatform.MacOs);

		Assert.That(MacroDeckDataRootResolver.Resolve(environment), Is.EqualTo(ChannelDefault(_baseDirectory)));
	}

	[Test]
	public void Resolve_uses_appdata_on_windows_when_packaged()
	{
		var appData = Path.Combine(Path.GetTempPath(), "roaming");
		var environment = CreateEnvironment(DataRootPlatform.Windows,
			packagedMarkerExists: true,
			environmentVariables: new Dictionary<string, string> { ["APPDATA"] = appData });

		Assert.That(MacroDeckDataRootResolver.Resolve(environment),
			Is.EqualTo(ChannelDefault(Path.Combine(appData, "MacroDeck"))));
	}

	[Test]
	public void Resolve_falls_back_to_home_appdata_on_windows_without_appdata_variable()
	{
		var environment = CreateEnvironment(DataRootPlatform.Windows, packagedMarkerExists: true);

		Assert.That(MacroDeckDataRootResolver.Resolve(environment),
			Is.EqualTo(ChannelDefault(Path.Combine(_homeDirectory, "AppData", "Roaming", "MacroDeck"))));
	}

	[Test]
	public void Resolve_uses_application_support_on_macos_when_packaged()
	{
		var environment = CreateEnvironment(DataRootPlatform.MacOs, packagedMarkerExists: true);

		Assert.That(MacroDeckDataRootResolver.Resolve(environment),
			Is.EqualTo(ChannelDefault(Path.Combine(_homeDirectory, "Library", "Application Support", "MacroDeck"))));
	}

	[Test]
	public void Resolve_uses_xdg_data_home_on_linux_when_packaged()
	{
		var xdgDataHome = Path.Combine(Path.GetTempPath(), "xdg-data");
		var environment = CreateEnvironment(DataRootPlatform.Linux,
			packagedMarkerExists: true,
			environmentVariables: new Dictionary<string, string> { ["XDG_DATA_HOME"] = xdgDataHome });

		Assert.That(MacroDeckDataRootResolver.Resolve(environment),
			Is.EqualTo(ChannelDefault(Path.Combine(xdgDataHome, "MacroDeck"))));
	}

	[Test]
	public void Resolve_falls_back_to_local_share_on_linux_without_xdg_variable()
	{
		var environment = CreateEnvironment(DataRootPlatform.Linux, packagedMarkerExists: true);

		Assert.That(MacroDeckDataRootResolver.Resolve(environment),
			Is.EqualTo(ChannelDefault(Path.Combine(_homeDirectory, ".local", "share", "MacroDeck"))));
	}

	[Test]
	public void Resolve_prefers_data_dir_override_over_portable_flag()
	{
		var overrideDir = Path.Combine(Path.GetTempPath(), "custom-data-root");
		var environment = CreateEnvironment(DataRootPlatform.Linux,
			packagedMarkerExists: true,
			environmentVariables: new Dictionary<string, string>
			{
				[MacroDeckDataRootResolver.DataDirEnvironmentVariable] = overrideDir,
				[MacroDeckDataRootResolver.PortableEnvironmentVariable] = "1"
			});

		Assert.That(MacroDeckDataRootResolver.Resolve(environment), Is.EqualTo(Path.GetFullPath(overrideDir)));
	}

	[Test]
	public void Resolve_prefers_the_new_data_directory_override()
	{
		var overrideDir = Path.Combine(Path.GetTempPath(), "new-custom-data-root");
		var environment = CreateEnvironment(DataRootPlatform.Linux,
			environmentVariables: new Dictionary<string, string>
			{
				[MacroDeckDataRootResolver.DataDirectoryEnvironmentVariable] = overrideDir,
				[MacroDeckDataRootResolver.DataDirEnvironmentVariable]
					= Path.Combine(Path.GetTempPath(), "old-custom-data-root")
			});

		Assert.That(MacroDeckDataRootResolver.Resolve(environment), Is.EqualTo(Path.GetFullPath(overrideDir)));
	}
}
