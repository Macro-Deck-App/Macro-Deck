using System.Diagnostics;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Application;

[SupportedOSPlatform("macos")]
internal sealed class MacOsApplicationService : ApplicationServiceBase
{
	protected override void StartProcess(string path, string? arguments, string? workingDirectory, bool runAsAdmin)
	{
		if (IsAppBundle(path))
		{
			var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
			startInfo.ArgumentList.Add("-a");
			startInfo.ArgumentList.Add(path);
			if (!string.IsNullOrWhiteSpace(arguments))
			{
				startInfo.ArgumentList.Add("--args");
				foreach (var argument in SplitArguments(arguments))
				{
					startInfo.ArgumentList.Add(argument);
				}
			}

			ApplyWorkingDirectory(startInfo, workingDirectory);
			Process.Start(startInfo);
			return;
		}

		base.StartProcess(path, arguments, workingDirectory, runAsAdmin);
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

	private static bool IsAppBundle(string path)
		=> path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase);

	private static string[] SplitArguments(string arguments)
		=> arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
