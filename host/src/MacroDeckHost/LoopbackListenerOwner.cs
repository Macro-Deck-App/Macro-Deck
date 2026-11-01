using System.Globalization;

namespace MacroDeckHost;

public enum ListenerOwner
{
	Unknown,
	CurrentUser,
	AnotherUser
}

public static class LoopbackListenerOwner
{
	private const string ListenState = "0A";

	public static ListenerOwner Of(int port)
	{
		return OperatingSystem.IsLinux() ? Of(port, File.ReadAllText) : ListenerOwner.Unknown;
	}

	public static ListenerOwner Of(int port, Func<string, string> readFile)
	{
		try
		{
			var table = readFile("/proc/net/tcp") + ReadIfPresent("/proc/net/tcp6", readFile);

			return Classify(FindListenerUid(table, port), ParseUid(readFile("/proc/self/status")));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return ListenerOwner.Unknown;
		}
	}

	// A kernel without IPv6 has no tcp6 table, and that must not hide the answer in the IPv4 one.
	private static string ReadIfPresent(string path, Func<string, string> readFile)
	{
		try
		{
			return readFile(path);
		}
		catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
		{
			return string.Empty;
		}
	}

	public static ListenerOwner Classify(long? listenerUid, long? ownUid)
	{
		if (listenerUid is null || ownUid is null)
		{
			return ListenerOwner.Unknown;
		}

		return listenerUid == ownUid ? ListenerOwner.CurrentUser : ListenerOwner.AnotherUser;
	}

	public static long? FindListenerUid(string procNetTcp, int port)
	{
		foreach (var line in procNetTcp.Split('\n'))
		{
			var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length < 8 || fields[3] != ListenState)
			{
				continue;
			}

			var separator = fields[1].LastIndexOf(':');
			if (separator >= 0
				&& int.TryParse(fields[1].AsSpan(separator + 1), NumberStyles.HexNumber, null, out var localPort)
				&& localPort == port
				&& long.TryParse(fields[7], out var uid))
			{
				return uid;
			}
		}

		return null;
	}

	public static long? ParseUid(string procSelfStatus)
	{
		var line = procSelfStatus.Split('\n')
			.FirstOrDefault(entry => entry.StartsWith("Uid:", StringComparison.Ordinal));
		var fields = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

		return fields is { Length: >= 3 } && long.TryParse(fields[2], out var uid) ? uid : null;
	}
}
