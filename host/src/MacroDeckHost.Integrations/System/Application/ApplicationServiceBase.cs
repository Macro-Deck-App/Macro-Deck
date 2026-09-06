using System.Diagnostics;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.System.Application;

internal abstract class ApplicationServiceBase : IApplicationService
{
	protected static readonly ILogger Logger = IntegrationLog.For<IApplicationService>(SystemIntegration.IntegrationId);

	public virtual bool IsSupported => true;

	public Task LaunchAsync(
		string path,
		string? arguments,
		string? workingDirectory,
		LaunchMode mode,
		bool runAsAdmin,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Task.CompletedTask;
		}

		if (mode != LaunchMode.Start && ProcessMatcher.IsRunning(path))
		{
			return mode == LaunchMode.StartStop
				? StopMatchingAsync(path)
				: FocusAsync(path, cancellationToken);
		}

		try
		{
			StartProcess(path, arguments, workingDirectory, runAsAdmin);
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to launch application '{Path}'", path);
		}

		return Task.CompletedTask;
	}

	public void OpenFile(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to open file '{Path}'", path);
		}
	}

	public void OpenWebsite(string url)
	{
		try
		{
			if (!IsWebsiteUrl(url))
			{
				throw new NotSupportedException(
					$"{url} is not a valid website URL. Only http:// and https:// URLs are supported.");
			}

			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to open website '{Url}'", url);
		}
	}

	internal static bool IsWebsiteUrl(string url)
		=> Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
			(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

	public abstract void OpenFolder(string path);

	public void Kill(string processName, bool graceful)
	{
		if (string.IsNullOrWhiteSpace(processName))
		{
			return;
		}

		var name = Path.GetFileNameWithoutExtension(processName);

		foreach (var process in Process.GetProcessesByName(name))
		{
			try
			{
				if (graceful && process.CloseMainWindow())
				{
					if (!process.WaitForExit(2000) && !process.HasExited)
					{
						process.Kill();
					}
				}
				else
				{
					process.Kill();
				}
			}
			catch (Exception exception)
			{
				Logger.Warning(exception, "Failed to kill process '{Process}'", name);
			}
		}
	}

	protected virtual void StartProcess(string path, string? arguments, string? workingDirectory, bool runAsAdmin)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = path,
			UseShellExecute = true
		};

		if (!string.IsNullOrEmpty(arguments))
		{
			startInfo.Arguments = arguments;
		}

		ApplyWorkingDirectory(startInfo, workingDirectory);
		Process.Start(startInfo);
	}

	protected static void ApplyWorkingDirectory(ProcessStartInfo startInfo, string? workingDirectory)
	{
		if (!string.IsNullOrWhiteSpace(workingDirectory))
		{
			startInfo.WorkingDirectory = workingDirectory;
		}
	}

	protected abstract Task FocusAsync(string path, CancellationToken cancellationToken);

	private static Task StopMatchingAsync(string path)
	{
		foreach (var process in ProcessMatcher.Find(path))
		{
			try
			{
				if (!process.CloseMainWindow())
				{
					process.Kill();
				}
			}
			catch (Exception exception)
			{
				Logger.Warning(exception, "Failed to stop application '{Path}'", path);
			}
		}

		return Task.CompletedTask;
	}
}
