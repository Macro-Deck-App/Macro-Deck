using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.DesktopEntries;

namespace MacroDeckHost.Integrations.System.Application;

[SupportedOSPlatform("linux")]
internal sealed class LinuxApplicationService : ApplicationServiceBase
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

		base.StartProcess(path, arguments, workingDirectory, runAsAdmin: false);
	}

	internal static bool RequiresPathLookup(string path)
		=> !string.IsNullOrWhiteSpace(path) &&
			path.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
			!File.Exists(path) &&
			!Directory.Exists(path);

	public override void OpenFolder(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
		startInfo.ArgumentList.Add(path);
		Process.Start(startInfo);
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
		Process.Start(startInfo);
	}

	private static string? JoinArguments(string? fromEntry, string? fromAction)
	{
		if (string.IsNullOrWhiteSpace(fromEntry))
		{
			return fromAction;
		}

		return string.IsNullOrWhiteSpace(fromAction) ? fromEntry : $"{fromEntry} {fromAction}";
	}

	private static void TryMakeExecutable(string path)
	{
		try
		{
			var startInfo = new ProcessStartInfo("chmod") { UseShellExecute = false };
			startInfo.ArgumentList.Add("+x");
			startInfo.ArgumentList.Add(path);
			Process.Start(startInfo)?.WaitForExit(2000);
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to chmod +x '{Path}'", path);
		}
	}
}
