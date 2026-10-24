using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeckHost.Integrations.AutoHotkey.Native;

internal static class AutoHotkeyWindowSpy
{
	public static string? Find(AutoHotkeyInstallation installation, Func<string, bool> exists)
	{
		return installation.WindowSpyCandidates.FirstOrDefault(exists);
	}

	// Window Spy is a tool window the user closes; it is not a tracked run and stays outside the job.
	public static bool Open(string executable, string script)
	{
		try
		{
			var startInfo = new ProcessStartInfo(executable)
			{
				UseShellExecute = false,
				WorkingDirectory = Path.GetDirectoryName(script) ?? string.Empty
			};
			startInfo.ArgumentList.Add(script);
			using var process = Process.Start(startInfo);
			return process is not null;
		}
		catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
		{
			return false;
		}
	}
}
