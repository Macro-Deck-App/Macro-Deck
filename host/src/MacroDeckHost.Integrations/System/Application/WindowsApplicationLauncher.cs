using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeckHost.Integrations.System.Application;

internal static class WindowsApplicationLauncher
{
	// App execution aliases (%LOCALAPPDATA%\Microsoft\WindowsApps) are zero-byte appexeclink reparse
	// points. ShellExecuteEx has to open the file to resolve its handler and fails with
	// ERROR_CANT_ACCESS_FILE, while CreateProcess resolves the link - so that failure, and only that
	// one, is retried without the shell. Elevation is not retried: CreateProcess cannot elevate, and
	// starting the application unelevated is not what the caller asked for.
	private const int ErrorCantAccessFile = 1920;

	public static void Start(
		string path,
		string? arguments,
		string? workingDirectory,
		bool runAsAdmin,
		Func<ProcessStartInfo, Process?> start)
	{
		try
		{
			start(BuildStartInfo(path, arguments, workingDirectory, useShellExecute: true, runAsAdmin));
		}
		catch (Win32Exception exception) when (!runAsAdmin && exception.NativeErrorCode == ErrorCantAccessFile)
		{
			start(BuildStartInfo(path, arguments, workingDirectory, useShellExecute: false, runAsAdmin: false));
		}
	}

	private static ProcessStartInfo BuildStartInfo(
		string path,
		string? arguments,
		string? workingDirectory,
		bool useShellExecute,
		bool runAsAdmin)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = path,
			UseShellExecute = useShellExecute
		};

		if (!string.IsNullOrEmpty(arguments))
		{
			startInfo.Arguments = arguments;
		}

		if (runAsAdmin)
		{
			startInfo.Verb = "runas";
		}

		if (!string.IsNullOrWhiteSpace(workingDirectory))
		{
			startInfo.WorkingDirectory = workingDirectory;
		}

		return startInfo;
	}
}
