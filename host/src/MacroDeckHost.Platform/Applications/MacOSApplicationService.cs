using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;
using MacroDeckHost.Platform.Processes;
using Serilog;

namespace MacroDeckHost.Platform.Applications;

[SupportedOSPlatform("macos")]
internal sealed class MacOsApplicationService(ILogger logger) : ApplicationServiceBase(logger)
{
	protected override void StartProcess(string path, string? arguments, string? workingDirectory, bool runAsAdmin)
	{
		if (MacOsBundles.IsAppBundle(path))
		{
			Process.Start(CreateBundleLaunch(path, arguments, workingDirectory));
			return;
		}

		base.StartProcess(path, arguments, workingDirectory, runAsAdmin);
	}

	internal static ProcessStartInfo CreateBundleLaunch(string path, string? arguments, string? workingDirectory)
	{
		var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
		startInfo.ArgumentList.Add("-a");
		startInfo.ArgumentList.Add(path);
		if (!string.IsNullOrWhiteSpace(arguments))
		{
			startInfo.ArgumentList.Add("--args");
			foreach (var argument in CommandLineArguments.Split(arguments))
			{
				startInfo.ArgumentList.Add(argument);
			}
		}

		ApplyWorkingDirectory(startInfo, workingDirectory);
		return startInfo;
	}

	// CloseMainWindow is always false on Unix, so without this every app would be stopped by SIGKILL and
	// lose unsaved work. Terminate lets it quit normally; only a process outside AppKit is killed.
	protected override void StopProcess(Process process)
	{
		if (!MacOsRunningApplication.TryTerminate(process.Id))
		{
			process.Kill();
		}
	}

	public override void OpenFolder(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
		startInfo.ArgumentList.Add(path);
		Process.Start(startInfo);
	}

	protected override Task FocusAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
			startInfo.ArgumentList.Add("-a");
			startInfo.ArgumentList.Add(path);
			Process.Start(startInfo);
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to focus application '{Path}'", path);
		}

		return Task.CompletedTask;
	}
}
