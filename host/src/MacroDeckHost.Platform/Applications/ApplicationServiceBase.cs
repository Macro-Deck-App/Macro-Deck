using System.Diagnostics;
using System.Text.RegularExpressions;
using MacroDeckHost.Platform.Processes;
using Serilog;

namespace MacroDeckHost.Platform.Applications;

internal abstract partial class ApplicationServiceBase(ILogger logger) : IApplicationService
{
	protected ILogger Logger { get; } = logger.ForContext<IApplicationService>();

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
			StartFile(path);
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

			StartWebsite(url);
		}
		catch (Exception exception)
		{
			Logger.Warning(exception, "Failed to open website '{Url}'", url);
		}
	}

	public void OpenUri(string uri)
	{
		if (!IsSteamUri(uri))
		{
			throw new NotSupportedException($"{uri} is not a steam:// URI.");
		}

		StartWebsite(uri);
	}

	protected virtual void StartFile(string path)
	{
		DetachedProcess.Start(new ProcessStartInfo(path) { UseShellExecute = true });
	}

	protected virtual void StartWebsite(string url)
	{
		DetachedProcess.Start(new ProcessStartInfo(url) { UseShellExecute = true });
	}

	internal static bool IsWebsiteUrl(string url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
	}

	internal static bool IsSteamUri(string? uri)
	{
		return uri is not null && SteamUriPattern().IsMatch(uri);
	}

	[GeneratedRegex(@"^steam://[A-Za-z0-9/_.\-]+$", RegexOptions.CultureInvariant)]
	private static partial Regex SteamUriPattern();

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
		DetachedProcess.Start(startInfo);
	}

	protected static void ApplyWorkingDirectory(ProcessStartInfo startInfo, string? workingDirectory)
	{
		if (!string.IsNullOrWhiteSpace(workingDirectory))
		{
			startInfo.WorkingDirectory = workingDirectory;
		}
	}

	protected abstract Task FocusAsync(string path, CancellationToken cancellationToken);

	private Task StopMatchingAsync(string path)
	{
		var processes = ProcessMatcher.Find(path);
		try
		{
			foreach (var process in processes)
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
		}
		finally
		{
			ProcessMatcher.Dispose(processes);
		}

		return Task.CompletedTask;
	}
}
