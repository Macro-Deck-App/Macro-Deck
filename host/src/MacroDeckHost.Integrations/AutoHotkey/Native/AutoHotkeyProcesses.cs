using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Integrations.AutoHotkey.Native;

internal sealed record AutoHotkeyStartInfo(
	string ExecutablePath,
	IReadOnlyList<string> Arguments,
	string WorkingDirectory);

internal interface IAutoHotkeyProcess : IDisposable
{
	int Id { get; }

	TextReader Output { get; }

	TextReader Error { get; }

	TextWriter Input { get; }

	bool HasExited { get; }

	int ExitCode { get; }

	Task WaitForExitAsync(CancellationToken cancellationToken);

	bool RequestClose();

	void Kill();
}

internal interface IAutoHotkeyLauncher : IDisposable
{
	IAutoHotkeyProcess Start(AutoHotkeyStartInfo info);
}

internal sealed class AutoHotkeyLauncher : IAutoHotkeyLauncher
{
	private static readonly UTF8Encoding _utf8 = new(false);

	private readonly Lock _gate = new();
	private WindowsJobObject? _job;
	private bool _disposed;

	public IAutoHotkeyProcess Start(AutoHotkeyStartInfo info)
	{
		var startInfo = new ProcessStartInfo(info.ExecutablePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardInputEncoding = _utf8,
			StandardOutputEncoding = _utf8,
			StandardErrorEncoding = _utf8,
			WorkingDirectory = info.WorkingDirectory
		};

		foreach (var argument in info.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
		}

		var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("AutoHotkey did not start.");

		// The process runs unassigned between its start and this call; a host crash in that window leaves
		// it running. Process.Start cannot create a suspended process, so the window is accepted.
		if (OperatingSystem.IsWindows() && !TryAssignToJob(process))
		{
			End(process);
			throw new InvalidOperationException("AutoHotkey could not be placed in the Macro Deck job.");
		}

		process.StandardInput.AutoFlush = true;
		return new LaunchedProcess(process);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			if (OperatingSystem.IsWindows())
			{
				_job?.Dispose();
			}

			_job = null;
		}
	}

	[SupportedOSPlatform("windows")]
	private bool TryAssignToJob(Process process)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return false;
			}

			_job ??= WindowsJobObject.Create();
			return _job.Assign(process);
		}
	}

	private static void End(Process process)
	{
		try
		{
			process.Kill(false);
			process.WaitForExit(TimeSpan.FromSeconds(2));
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
		{
		}
		finally
		{
			process.Dispose();
		}
	}

	private sealed class LaunchedProcess : IAutoHotkeyProcess
	{
		private readonly Process _process;

		public LaunchedProcess(Process process)
		{
			_process = process;
			Id = process.Id;
		}

		public int Id { get; }

		public TextReader Output => _process.StandardOutput;

		public TextReader Error => _process.StandardError;

		public TextWriter Input => _process.StandardInput;

		public bool HasExited => _process.HasExited;

		public int ExitCode => _process.ExitCode;

		public Task WaitForExitAsync(CancellationToken cancellationToken)
		{
			return _process.WaitForExitAsync(cancellationToken);
		}

		public bool RequestClose()
		{
			return OperatingSystem.IsWindows() && !_process.HasExited && AutoHotkeyWindows.PostClose(Id);
		}

		public void Kill()
		{
			try
			{
				if (!_process.HasExited)
				{
					_process.Kill(false);
				}
			}
			catch (InvalidOperationException)
			{
			}
			catch (Win32Exception)
			{
			}
		}

		public void Dispose()
		{
			_process.Dispose();
		}
	}
}
