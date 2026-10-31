using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost;

public static class HostEndpoints
{
	public static int PublicPort => ResolvedPublicEndpoints.Value.PublicPort;

	public static int? PublicHttpsPort => ResolvedPublicEndpoints.Value.HttpsPort;

	public static bool IsPublicPort(int port)
	{
		return ResolvedPublicEndpoints.Value.IsPublicPort(port);
	}

	public static bool TryGetLoopbackPort(out int port)
	{
		port = ResolvedLoopbackPort.Value ?? 0;
		return port > 0;
	}

	public const string LoopbackPortEnvironmentVariable = "MACRODECK_HOST_PORT";

	public const int DevelopmentLoopbackPort = 5191;

	public static string LoopbackPortFilePath { get; }
		= Path.Combine(Path.GetTempPath(), BuildConfig.LoopbackPortFileName);

	public static string? PerUserLoopbackPortFilePath { get; } = ResolvePerUserLoopbackPortFilePath();

	public static IReadOnlyList<string> LoopbackPortFilePaths { get; } = PerUserLoopbackPortFilePath is { } perUser
		? [perUser, LoopbackPortFilePath]
		: [LoopbackPortFilePath];

	// Keep in sync with per_user_port_dir in ui/bootstrapper/src/host.rs and HostDiscovery in the plugin CLI.
	// A custom TMPDIR stays authoritative: that is how parallel hosts isolate their single-instance check.
	public static string? ResolvePerUserDirectory(
		bool isLinux,
		string temporaryDirectory,
		Func<string, string?> environment)
	{
		if (!isLinux || temporaryDirectory.TrimEnd('/') != "/tmp")
		{
			return null;
		}

		var configHome = environment("XDG_CONFIG_HOME");
		if (configHome is not null && Path.IsPathRooted(configHome))
		{
			return Path.Combine(configHome, "macro-deck");
		}

		var home = environment("HOME");

		return home is not null && Path.IsPathRooted(home) ? Path.Combine(home, ".config", "macro-deck") : null;
	}

	private static string? ResolvePerUserLoopbackPortFilePath()
	{
		var directory = ResolvePerUserDirectory(OperatingSystem.IsLinux(),
			Path.GetTempPath(),
			Environment.GetEnvironmentVariable);

		return directory is null ? null : Path.Combine(directory, BuildConfig.LoopbackPortFileName);
	}

	public static int ResolveLoopbackPort()
	{
		return ResolveLoopbackPort(Environment.GetEnvironmentVariable(LoopbackPortEnvironmentVariable));
	}

	public static int ResolveLoopbackPort(string? configured)
	{
		if (int.TryParse(configured, out var port) && port is > 0 and <= ushort.MaxValue)
		{
			return port;
		}

		return BuildConfig.Channel == BuildChannel.Development ? DevelopmentLoopbackPort : 0;
	}
}
