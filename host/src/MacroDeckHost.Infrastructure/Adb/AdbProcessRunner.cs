using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed record AdbProcessResult(
	bool Started,
	int ExitCode,
	string StandardOutput,
	string StandardError,
	bool TimedOut);

internal sealed record AdbBinaryResult(
	bool Started,
	int ExitCode,
	byte[] StandardOutput,
	string StandardError,
	bool TimedOut);

internal interface IAdbProcessRunner
{
	Task<AdbProcessResult> RunAsync(
		string executablePath,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken);

	Task<AdbBinaryResult> RunBinaryAsync(
		string executablePath,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken);

	Task DrainAsync(TimeSpan budget);
}

internal sealed class AdbProcessRunner : IAdbProcessRunner, IDisposable
{
	private readonly ConcurrentDictionary<int, Process> _liveProcesses = new();
	private readonly ILogger _logger;

	public AdbProcessRunner(ILogger logger)
	{
		_logger = logger.ForContext<AdbProcessRunner>();
	}

	public async Task<AdbProcessResult> RunAsync(
		string executablePath,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		Process process;
		try
		{
			process = StartProcess(executablePath, arguments);
		}
		catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
		{
			_logger.Debug(ex, "Failed to start {Executable}", executablePath);
			return new AdbProcessResult(false, -1, string.Empty, string.Empty, false);
		}

		_liveProcesses[process.Id] = process;
		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(timeout);

			var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
			var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

			try
			{
				await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cts.Token));
				return new AdbProcessResult(true, process.ExitCode, outputTask.Result, errorTask.Result, false);
			}
			catch (OperationCanceledException)
			{
				KillQuietly(process);
				return new AdbProcessResult(true, -1, string.Empty, string.Empty, true);
			}
		}
		finally
		{
			_liveProcesses.TryRemove(process.Id, out _);
			process.Dispose();
		}
	}

	public async Task<AdbBinaryResult> RunBinaryAsync(
		string executablePath,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		Process process;
		try
		{
			process = StartProcess(executablePath, arguments);
		}
		catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
		{
			_logger.Debug(ex, "Failed to start {Executable}", executablePath);
			return new AdbBinaryResult(false, -1, [], string.Empty, false);
		}

		_liveProcesses[process.Id] = process;
		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(timeout);

			// A StreamReader would decode the PNG bytes as text and corrupt them on re-encoding, so the
			// raw stream is copied byte-for-byte instead.
			var outputStream = new MemoryStream();
			var copyTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream, cts.Token);
			var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

			try
			{
				await Task.WhenAll(copyTask, errorTask, process.WaitForExitAsync(cts.Token));
				return new AdbBinaryResult(true, process.ExitCode, outputStream.ToArray(), errorTask.Result, false);
			}
			catch (OperationCanceledException)
			{
				KillQuietly(process);
				return new AdbBinaryResult(true, -1, [], string.Empty, true);
			}
		}
		finally
		{
			_liveProcesses.TryRemove(process.Id, out _);
			process.Dispose();
		}
	}

	public async Task DrainAsync(TimeSpan budget)
	{
		var processes = _liveProcesses.Values.ToList();
		if (processes.Count == 0)
		{
			return;
		}

		try
		{
			using var cts = new CancellationTokenSource(budget);
			await Task.WhenAll(processes.Select(process => WaitQuietlyAsync(process, cts.Token)));
		}
		catch
		{
		}

		foreach (var process in processes)
		{
			KillQuietly(process);
		}
	}

	public void Dispose()
	{
		foreach (var process in _liveProcesses.Values)
		{
			try
			{
				process.Dispose();
			}
			catch (InvalidOperationException)
			{
			}
		}

		_liveProcesses.Clear();
	}

	private static Process StartProcess(string executablePath, IReadOnlyList<string> arguments)
	{
		var startInfo = new ProcessStartInfo(executablePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");
	}

	private static async Task WaitQuietlyAsync(Process process, CancellationToken cancellationToken)
	{
		try
		{
			await process.WaitForExitAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException
			or InvalidOperationException
			or ObjectDisposedException)
		{
		}
	}

	private void KillQuietly(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
		{
			_logger.Debug(ex, "Failed to kill adb process {ProcessId}", process.Id);
		}
	}
}
