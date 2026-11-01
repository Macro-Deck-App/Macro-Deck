using Serilog;

namespace MacroDeckHost;

public static class SingleInstanceGuard
{
	public static int? ParsePort(string? portFileContent)
	{
		return int.TryParse(portFileContent?.Trim(), out var port) && port is > 0 and <= ushort.MaxValue
			? port
			: null;
	}

	public static Task<bool> IsAnotherInstanceRunning()
	{
		return IsAnotherInstanceRunning(HostEndpoints.LoopbackPortFilePaths,
			port => LoopbackListenerOwner.Of(port) == ListenerOwner.AnotherUser,
			IsAnswering);
	}

	internal static async Task<bool> IsAnotherInstanceRunning(
		IEnumerable<string> portFilePaths,
		Func<int, bool> belongsToAnotherUser,
		Func<int, Task<bool>> isAnswering)
	{
		foreach (var path in portFilePaths)
		{
			if (await ReadPort(path) is not { } port)
			{
				continue;
			}

			if (belongsToAnotherUser(port))
			{
				Log.Debug("The listener on port {Port} from {File} belongs to another user", port, path);
				continue;
			}

			if (await isAnswering(port))
			{
				return true;
			}
		}

		return false;
	}

	private static async Task<int?> ReadPort(string path)
	{
		try
		{
			return ParsePort(await File.ReadAllTextAsync(path));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static async Task<bool> IsAnswering(int port)
	{
		try
		{
			using var client = new HttpClient();
			client.Timeout = TimeSpan.FromSeconds(2);
			var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/api/auth/status"));
			return response.IsSuccessStatusCode;
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
		{
			Log.Debug("Stale port file found (no host answering on port {Port})", port);
			return false;
		}
	}
}
