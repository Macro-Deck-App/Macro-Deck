using System.Globalization;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// Finds the loopback endpoint of an already-running desktop host, so <c>run</c> can default to the real
/// thing without the developer having to look a port up.
/// <para>
/// The host publishes the loopback port it actually bound to a file in the temp directory once its
/// listener is up (<c>LoopbackPortFileService</c>) and deletes it on shutdown; that file is how the
/// desktop bootstrapper discovers the host too. Where the temp directory is the shared Linux <c>/tmp</c>,
/// a current host writes the same file to a per-user directory as well, which is read first; the temp
/// file still finds an older host, and a file whose listener belongs to another user or is gone is
/// skipped. The port itself is the same one the supervisor injects as
/// <c>MACRO_DECK_PLUGIN_HOST_URL</c>, so a plugin launched against it sees exactly the endpoint a managed
/// plugin would. The file name differs per build channel and this CLI ships independently of any host
/// build, so both names are probed; when both exist - a Development and a Production host running side
/// by side - the most recently written one wins, which is the host the developer started last.
/// </para>
/// </summary>
internal static class HostDiscovery
{
	public const string ProductionPortFileName = "macro-deck-host.port";

	public const string DevelopmentPortFileName = "macro-deck-host-development.port";

	public static IReadOnlyList<string> ProbedPaths(string directory)
	{
		return [Path.Combine(directory, ProductionPortFileName), Path.Combine(directory, DevelopmentPortFileName)];
	}

	// Keep in sync with HostEndpoints.ResolvePerUserDirectory in the host.
	internal static string? PerUserDirectory(
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

	public static IReadOnlyList<string> SearchedPaths()
	{
		return PerUserDirectoryOfThisProcess() is { } perUser
			? [.. ProbedPaths(perUser), .. ProbedPaths(Path.GetTempPath())]
			: ProbedPaths(Path.GetTempPath());
	}

	public static DiscoveredHost? Discover()
	{
		return Discover(PerUserDirectoryOfThisProcess(), Path.GetTempPath(), LoopbackListener.Inspect);
	}

	internal static DiscoveredHost? Discover(
		string? perUserDirectory,
		string temporaryDirectory,
		Func<int, ListenerState> inspect)
	{
		if (perUserDirectory is null)
		{
			return Discover(temporaryDirectory);
		}

		bool Accept(int port)
		{
			return inspect(port) is ListenerState.Unknown or ListenerState.Ours;
		}

		return Discover(perUserDirectory, Accept) ?? Discover(temporaryDirectory, Accept);
	}

	internal static DiscoveredHost? Discover(string directory)
	{
		return Discover(directory, _ => true);
	}

	private static DiscoveredHost? Discover(string directory, Func<int, bool> accept)
	{
		DiscoveredHost? best = null;
		var bestWrittenAt = DateTime.MinValue;

		foreach (var path in ProbedPaths(directory))
		{
			if (!TryReadPort(path, out var port) || !accept(port))
			{
				continue;
			}

			var writtenAt = File.GetLastWriteTimeUtc(path);
			if (best is not null && writtenAt <= bestWrittenAt)
			{
				continue;
			}

			best = new DiscoveredHost($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}", path);
			bestWrittenAt = writtenAt;
		}

		return best;
	}

	private static string? PerUserDirectoryOfThisProcess()
	{
		return PerUserDirectory(OperatingSystem.IsLinux(), Path.GetTempPath(), Environment.GetEnvironmentVariable);
	}

	private static bool TryReadPort(string path, out int port)
	{
		port = 0;

		string content;
		try
		{
			content = File.ReadAllText(path);
		}
		catch (Exception ex) when (ex is IOException
			or UnauthorizedAccessException
			or NotSupportedException
			or ArgumentException)
		{
			return false;
		}

		return int.TryParse(content.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
			&& port is > 0 and <= ushort.MaxValue;
	}
}

internal enum ListenerState
{
	Unknown,
	Ours,
	Foreign,
	Gone
}

// The CLI ships on its own and references no host code, so this is a small copy of LoopbackListenerOwner.
internal static class LoopbackListener
{
	private const string ListenStateCode = "0A";

	public static ListenerState Inspect(int port)
	{
		return OperatingSystem.IsLinux() ? Inspect(port, File.ReadAllText) : ListenerState.Unknown;
	}

	public static ListenerState Inspect(int port, Func<string, string> readFile)
	{
		try
		{
			return Classify(readFile("/proc/net/tcp") + ReadIfPresent("/proc/net/tcp6", readFile),
				readFile("/proc/self/status"),
				port);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return ListenerState.Unknown;
		}
	}

	// A kernel without IPv6 has no tcp6 table, and that must not hide the answer in the IPv4 one.
	private static string ReadIfPresent(string path, Func<string, string> readFile)
	{
		try
		{
			return readFile(path);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return string.Empty;
		}
	}

	public static ListenerState Classify(string procNetTcp, string procSelfStatus, int port)
	{
		var ownUid = procSelfStatus.Split('\n')
			.Where(line => line.StartsWith("Uid:", StringComparison.Ordinal))
			.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
			.Where(fields => fields.Length >= 3)
			.Select(fields => fields[2])
			.FirstOrDefault();
		if (ownUid is null || !procNetTcp.Contains("local_address", StringComparison.Ordinal))
		{
			return ListenerState.Unknown;
		}

		foreach (var line in procNetTcp.Split('\n'))
		{
			var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length < 8 || fields[3] != ListenStateCode)
			{
				continue;
			}

			var separator = fields[1].LastIndexOf(':');
			if (separator >= 0
				&& int.TryParse(fields[1].AsSpan(separator + 1), NumberStyles.HexNumber, null, out var localPort)
				&& localPort == port)
			{
				return fields[7] == ownUid ? ListenerState.Ours : ListenerState.Foreign;
			}
		}

		return ListenerState.Gone;
	}
}

/// <summary>A running host's loopback endpoint and the port file it was read from - the path is carried
/// along so <c>run</c> can name its source, since "which host did it pick" is otherwise invisible.</summary>
internal readonly record struct DiscoveredHost(string Url, string PortFilePath);
