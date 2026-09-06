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

	public static async Task<bool> IsAnotherInstanceRunning()
	{
		string content;
		try
		{
			content = await File.ReadAllTextAsync(HostEndpoints.LoopbackPortFilePath);
		}
		catch (IOException)
		{
			return false;
		}

		var port = ParsePort(content);
		if (port is null)
		{
			return false;
		}

		try
		{
			using var client = new HttpClient();
			client.Timeout = TimeSpan.FromSeconds(2);
			var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/api/system/version"));
			return response.IsSuccessStatusCode;
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
		{
			Log.Debug("Stale port file found (no host answering on port {Port})", port);
			return false;
		}
	}
}
