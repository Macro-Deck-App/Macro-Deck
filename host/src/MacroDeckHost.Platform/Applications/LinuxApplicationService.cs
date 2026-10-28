using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.DesktopEntries;
using MacroDeckHost.Platform.Processes;
using Serilog;

namespace MacroDeckHost.Platform.Applications;

[SupportedOSPlatform("linux")]
internal sealed class LinuxApplicationService(ILogger logger) : ApplicationServiceBase(logger)
{
	private const int MaxDesktopEntryLines = 2048;

	protected override void StartProcess(string path, string? arguments, string? workingDirectory, bool runAsAdmin)
	{
		// A desktop entry is a description, not a program: nothing runs it, so run what its Exec line
		// names instead. A dropped entry is already resolved before it reaches the action (issue #395);
		// this covers one that was browsed to or typed in.
		if (DesktopEntryParser.IsDesktopEntry(path) && File.Exists(path))
		{
			var command = DesktopEntryParser.TryReadCommand(File.ReadLines(path).Take(MaxDesktopEntryLines));
			if (command is not null)
			{
				StartCommand(command.Program, JoinArguments(command.Arguments, arguments), workingDirectory);
				return;
			}
		}

		if (RequiresPathLookup(path))
		{
			StartCommand(path, arguments, workingDirectory);
			return;
		}

		if (path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
		{
			TryMakeExecutable(path);
		}

		base.StartProcess(path, arguments, workingDirectory, false);
	}

	internal static bool RequiresPathLookup(string path)
	{
		return !string.IsNullOrWhiteSpace(path)
			&& path.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0
			&& !File.Exists(path)
			&& !Directory.Exists(path);
	}

	public override void OpenFolder(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var startInfo = AppImageEnvironment.TryCreateOpener(path);
		if (startInfo is null)
		{
			startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
			startInfo.ArgumentList.Add(path);
		}

		DetachedProcess.Start(startInfo);
	}

	protected override void StartFile(string path)
	{
		var opener = OpensWithDefaultApplication(path) ? AppImageEnvironment.TryCreateOpener(path) : null;
		if (opener is null)
		{
			base.StartFile(path);
			return;
		}

		DetachedProcess.Start(opener);
	}

	protected override void StartWebsite(string url)
	{
		var opener = AppImageEnvironment.TryCreateOpener(url);
		if (opener is null)
		{
			base.StartWebsite(url);
			return;
		}

		DetachedProcess.Start(opener);
	}

	internal static bool OpensWithDefaultApplication(string target)
	{
		var path = target;
		if (!Path.IsPathRooted(target))
		{
			if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
			{
				return false;
			}

			if (!uri.IsFile)
			{
				return true;
			}

			path = uri.LocalPath;
		}

		return !AppImageEnvironment.IsExecutableFile(path);
	}

	protected override Task FocusAsync(string path, CancellationToken cancellationToken)
	{
		var name = Path.GetFileNameWithoutExtension(path);
		if (string.IsNullOrEmpty(name))
		{
			return Task.CompletedTask;
		}

		try
		{
			var startInfo = new ProcessStartInfo("wmctrl") { UseShellExecute = false };
			startInfo.ArgumentList.Add("-xa");
			startInfo.ArgumentList.Add(name);
			AppImageEnvironment.RestoreOutside(startInfo);
			Process.Start(startInfo);
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to focus application '{Path}' (is wmctrl installed?)", path);
		}

		return Task.CompletedTask;
	}

	private static void StartCommand(string program, string? arguments, string? workingDirectory)
	{
		var startInfo = new ProcessStartInfo(program) { UseShellExecute = false };
		if (!string.IsNullOrEmpty(arguments))
		{
			startInfo.Arguments = arguments;
		}

		ApplyWorkingDirectory(startInfo, workingDirectory);
		DetachedProcess.Start(startInfo);
	}

	private static string? JoinArguments(string? fromEntry, string? fromAction)
	{
		if (string.IsNullOrWhiteSpace(fromEntry))
		{
			return fromAction;
		}

		return string.IsNullOrWhiteSpace(fromAction) ? fromEntry : $"{fromEntry} {fromAction}";
	}

	private void TryMakeExecutable(string path)
	{
		try
		{
			var startInfo = new ProcessStartInfo("chmod") { UseShellExecute = false };
			startInfo.ArgumentList.Add("+x");
			startInfo.ArgumentList.Add(path);
			AppImageEnvironment.RestoreOutside(startInfo);
			Process.Start(startInfo)?.WaitForExit(2000);
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to chmod +x '{Path}'", path);
		}
	}
}
