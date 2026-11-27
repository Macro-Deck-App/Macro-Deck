using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Integrations.Clipboard.Native;

internal readonly record struct ClipboardCommand(string FileName, IReadOnlyList<string> Arguments);

internal readonly record struct ClipboardProcessResult(int ExitCode, byte[] Output, bool IsTruncated, string Error)
{
	public bool Succeeded => ExitCode == 0 && !IsTruncated;
}

internal static class ClipboardProcess
{
	private const int ErrorLimit = 4096;
	private const int ChunkSize = 64 * 1024;

	public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

	public static async Task<ClipboardProcessResult> ReadAsync(
		ClipboardCommand command,
		int maxOutputBytes,
		CancellationToken cancellationToken)
	{
		var startInfo = CreateStartInfo(command.FileName, command.Arguments);
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(CallTimeout);
		using var process = Start(startInfo);
		using var killOnTimeout = timeout.Token.Register(() => Kill(process));
		var errorTask = ReadCappedTextAsync(process.StandardError.BaseStream, timeout.Token);
		try
		{
			var (output, truncated) = await ReadCappedAsync(process.StandardOutput.BaseStream,
					maxOutputBytes,
					timeout.Token)
				.ConfigureAwait(false);
			if (truncated)
			{
				Kill(process);
				return new ClipboardProcessResult(-1, output, true, string.Empty);
			}

			var error = await errorTask.ConfigureAwait(false);
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
			return new ClipboardProcessResult(process.ExitCode, output, false, error);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			Kill(process);
			throw new ClipboardException(ClipboardFailure.Failed, $"{command.FileName} did not finish in time");
		}
		catch (OperationCanceledException)
		{
			Kill(process);
			throw;
		}
	}

	// Copy tools fork a server that keeps the selection, so their output goes to /dev/null: a pipe
	// held by that server would never reach end of file.
	public static async Task<int> WriteAsync(
		ClipboardCommand command,
		byte[] input,
		CancellationToken cancellationToken)
	{
		var arguments = new List<string> { "-c", "exec \"$0\" \"$@\" >/dev/null 2>&1", command.FileName };
		arguments.AddRange(command.Arguments);
		var startInfo = CreateStartInfo("/bin/sh", arguments);
		startInfo.RedirectStandardInput = true;

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(CallTimeout);
		using var process = Start(startInfo);
		using var killOnTimeout = timeout.Token.Register(() => Kill(process));
		try
		{
			var stdin = process.StandardInput.BaseStream;
			await stdin.WriteAsync(input, timeout.Token).ConfigureAwait(false);
			await stdin.FlushAsync(timeout.Token).ConfigureAwait(false);
			process.StandardInput.Close();
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
			return process.ExitCode;
		}
		catch (IOException exception)
		{
			Kill(process);
			throw new ClipboardException(ClipboardFailure.Failed,
				$"{command.FileName} closed its input early",
				exception);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			Kill(process);
			throw new ClipboardException(ClipboardFailure.Failed, $"{command.FileName} did not finish in time");
		}
		catch (OperationCanceledException)
		{
			Kill(process);
			throw;
		}
	}

	// A long-running watcher: no call timeout, the caller owns its lifetime and must drain stderr.
	public static Process StartStreaming(ClipboardCommand command)
	{
		try
		{
			return ProcessRunner.StartStreaming(command.FileName, command.Arguments, null);
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
		{
			throw new ClipboardException(ClipboardFailure.Unavailable,
				$"{command.FileName} could not be started",
				exception);
		}
	}

	public static void Kill(Process process)
	{
		try
		{
			process.Kill(true);
		}
		catch (Exception exception) when (exception is InvalidOperationException
			or Win32Exception
			or NotSupportedException)
		{
		}
	}

	private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
	{
		var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		AppImageEnvironment.RestoreOutside(startInfo);
		return startInfo;
	}

	private static Process Start(ProcessStartInfo startInfo)
	{
		try
		{
			return Process.Start(startInfo) ?? throw new ClipboardException(ClipboardFailure.Unavailable);
		}
		catch (Win32Exception exception)
		{
			throw new ClipboardException(ClipboardFailure.Unavailable,
				$"{startInfo.FileName} could not be started",
				exception);
		}
	}

	private static async Task<(byte[] Output, bool Truncated)> ReadCappedAsync(
		Stream stream,
		int maxBytes,
		CancellationToken cancellationToken)
	{
		using var output = new MemoryStream();
		var buffer = new byte[ChunkSize];
		while (true)
		{
			var wanted = (int)Math.Min(buffer.Length, (long)maxBytes - output.Length + 1);
			var read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				return (output.ToArray(), false);
			}

			if (output.Length + read > maxBytes)
			{
				return ([], true);
			}

			output.Write(buffer, 0, read);
		}
	}

	private static async Task<string> ReadCappedTextAsync(Stream stream, CancellationToken cancellationToken)
	{
		try
		{
			var buffer = new byte[ErrorLimit];
			var kept = 0;
			var scratch = new byte[ChunkSize];
			while (true)
			{
				var read = await stream.ReadAsync(scratch, cancellationToken).ConfigureAwait(false);
				if (read == 0)
				{
					return Encoding.UTF8.GetString(buffer, 0, kept);
				}

				var copied = Math.Min(read, ErrorLimit - kept);
				Array.Copy(scratch, 0, buffer, kept, copied);
				kept += copied;
			}
		}
		catch (Exception exception) when (exception is IOException
			or ObjectDisposedException
			or OperationCanceledException)
		{
			return string.Empty;
		}
	}
}
