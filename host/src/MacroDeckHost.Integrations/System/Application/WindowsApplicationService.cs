using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.System.Application;

[SupportedOSPlatform("windows")]
internal sealed class WindowsApplicationService : ApplicationServiceBase
{
	// A path matching several processes must not multiply the wait: every attempt shares one budget.
	private const int ActivationBudgetMs = 500;
	private const int MinimumVerifyMs = 100;

	protected override void StartProcess(string path, string? arguments, string? workingDirectory, bool runAsAdmin)
		=> WindowsApplicationLauncher.Start(path, arguments, workingDirectory, runAsAdmin, Process.Start);

	public override void OpenFolder(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
	}

	protected override Task FocusAsync(string path, CancellationToken cancellationToken)
	{
		var activated = false;
		var deadline = Environment.TickCount64 + ActivationBudgetMs;

		foreach (var process in ProcessMatcher.Find(path))
		{
			try
			{
				var remaining = (int)Math.Clamp(deadline - Environment.TickCount64,
					MinimumVerifyMs,
					ActivationBudgetMs);
				var handle = process.MainWindowHandle;
				if (handle != IntPtr.Zero && Win32Foreground.TryActivate(handle, remaining))
				{
					activated = true;
					break;
				}
			}
			catch (Exception exception)
			{
				Logger.Warning(exception, "Failed to focus application '{Path}'", path);
			}
		}

		if (!activated)
		{
			Logger.Warning("Could not bring any window of application '{Path}' to the foreground", path);
		}

		return Task.CompletedTask;
	}
}
