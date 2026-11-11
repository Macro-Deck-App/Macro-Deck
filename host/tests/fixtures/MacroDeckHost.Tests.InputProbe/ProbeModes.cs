using System.Globalization;
using System.Runtime.InteropServices;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Platform.Applications;
using Serilog;

namespace MacroDeckHost.Tests.InputProbe;

internal static class ProbeModes
{
	public static int Idle(ProbeOptions options)
	{
		if (options.Get("pid-file") is { } pidFile)
		{
			File.WriteAllText(pidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
		}

		Thread.Sleep(Timeout.Infinite);
		return 0;
	}

	public static async Task<int> LaunchAsync(ProbeOptions options)
	{
		if (options.Rest.Length == 0)
		{
			ProbeOutput.Error("launch needs the program to start after --");
			return 2;
		}

		if (options.Get("own-group") == "true" && !OperatingSystem.IsWindows() && setpgid(0, 0) != 0)
		{
			ProbeOutput.Error("setpgid failed");
			return 1;
		}

		var service = ApplicationServiceFactory.Create(Log.Logger);
		var arguments = options.Rest.Length > 1 ? string.Join(' ', options.Rest.Skip(1).Select(Quote)) : null;
		await service.LaunchAsync(options.Rest[0], arguments, null, LaunchMode.Start, false);
		ProbeOutput.Write(new ProbeEvent { Type = "launched", Pid = Environment.ProcessId });
		Thread.Sleep(Timeout.Infinite);
		return 0;
	}

	public static async Task<int> SendAsync(ProbeOptions options)
	{
		var provider = KeyboardInputProviderFactory.Create();
		try
		{
			var service = new KeyboardInputService(provider, new KeyboardLayoutService(), Log.Logger);
			await service.TypeTextAsync(options.Get("text") ?? string.Empty);
			ProbeOutput.Write(new ProbeEvent { Type = "sent", Pid = Environment.ProcessId });
			return 0;
		}
		finally
		{
			(provider as IDisposable)?.Dispose();
		}
	}

	[DllImport("libc", SetLastError = true)]
	private static extern int setpgid(int pid, int processGroup);

	private static string Quote(string argument)
	{
		return argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
	}
}
