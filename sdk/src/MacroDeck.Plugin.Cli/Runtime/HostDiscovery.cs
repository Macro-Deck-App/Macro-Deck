using System.Globalization;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// Finds the loopback endpoint of an already-running desktop host, so <c>run</c> can default to the real
/// thing without the developer having to look a port up.
/// <para>
/// The host publishes the loopback port it actually bound to a file in the temp directory once its
/// listener is up (<c>LoopbackPortFileService</c>) and deletes it on shutdown; that file is how the
/// desktop bootstrapper discovers the host too. The port itself is the same one the supervisor injects
/// as <c>MACRO_DECK_PLUGIN_HOST_URL</c>, so a plugin launched against it sees exactly the endpoint a
/// managed plugin would. The file name differs per build channel and this CLI ships independently of any
/// host build, so both names are probed; when both exist - a Development and a Production host running
/// side by side - the most recently written one wins, which is the host the developer started last.
/// </para>
/// </summary>
internal static class HostDiscovery
{
	public const string ProductionPortFileName = "macro-deck-host.port";

	public const string DevelopmentPortFileName = "macro-deck-host-development.port";

	public static IReadOnlyList<string> ProbedPaths(string directory) =>
		[Path.Combine(directory, ProductionPortFileName), Path.Combine(directory, DevelopmentPortFileName)];

	public static DiscoveredHost? Discover() => Discover(Path.GetTempPath());

	internal static DiscoveredHost? Discover(string directory)
	{
		DiscoveredHost? best = null;
		var bestWrittenAt = DateTime.MinValue;

		foreach (var path in ProbedPaths(directory))
		{
			if (!TryReadPort(path, out var port))
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

		return int.TryParse(content.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
			port is > 0 and <= ushort.MaxValue;
	}
}

/// <summary>A running host's loopback endpoint and the port file it was read from - the path is carried
/// along so <c>run</c> can name its source, since "which host did it pick" is otherwise invisible.</summary>
internal readonly record struct DiscoveredHost(string Url, string PortFilePath);
