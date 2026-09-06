using System.Diagnostics;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// A plugin child process <c>run</c> launched and streams output from, prefixed live via
/// <see cref="Process.OutputDataReceived" />/<see cref="Process.ErrorDataReceived" /> rather than by
/// polling - unlike <c>ExternalPlugin</c>, which only ever buffers into a string. <c>run</c> needs live
/// streaming and, for self-registering mode, a process <c>MacroDeckTestHost.LaunchAsync</c> cannot
/// produce (it only ever launches managed), so this is a thin, deliberately separate wrapper rather than
/// a reuse of that internal type.
/// </summary>
internal sealed class SupervisedPluginProcess : IAsyncDisposable
{
	private readonly Process _process;

	private SupervisedPluginProcess(Process process)
	{
		_process = process;
	}

	public int ProcessId => _process.Id;

	public bool HasExited => _process.HasExited;

	public int ExitCode => _process.ExitCode;

	/// <summary>
	/// Starts <paramref name="spec" /> with exactly <paramref name="environment" /> as its child
	/// environment - not layered on top of whatever <see cref="ProcessStartInfo.Environment" /> already
	/// auto-inherited from this process, since that would leave a stale <c>MACRO_DECK_PLUGIN_*</c> or
	/// <c>ASPNETCORE_URLS</c> value in place whenever <paramref name="environment" /> simply does not
	/// mention that exact key (which is exactly what a correctly scrubbed
	/// <see cref="PluginEnvironmentComposer" /> result does for anything it dropped). Every
	/// <see cref="PluginEnvironmentComposer.IsScrubbed" /> key is removed first, unconditionally, then
	/// <paramref name="environment" /> is applied on top.
	/// </summary>
	public static SupervisedPluginProcess Start(PluginLaunchSpec spec,
		IReadOnlyDictionary<string, string?> environment,
		Action<string> onOutputLine,
		Action<string> onErrorLine)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = spec.ExecutablePath,
			WorkingDirectory = spec.WorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		foreach (var argument in spec.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var key in startInfo.Environment.Keys.Where(PluginEnvironmentComposer.IsScrubbed).ToList())
		{
			startInfo.Environment.Remove(key);
		}

		foreach (var (name, value) in environment)
		{
			if (value is null)
			{
				startInfo.Environment.Remove(name);
			}
			else
			{
				startInfo.Environment[name] = value;
			}
		}

		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data is not null)
			{
				onOutputLine(e.Data);
			}
		};

		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is not null)
			{
				onErrorLine(e.Data);
			}
		};

		if (!process.Start())
		{
			throw new InvalidOperationException($"Failed to start '{spec.ExecutablePath}'.");
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		return new SupervisedPluginProcess(process);
	}

	public Task WaitForExitAsync(CancellationToken cancellationToken = default)
		=> _process.WaitForExitAsync(cancellationToken);

	/// <summary>Kills the process (and any children it spawned), tolerating one that already exited.</summary>
	public void Kill()
	{
		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
			// Exited between the check and the call.
		}
	}

	public async ValueTask DisposeAsync()
	{
		Kill();

		try
		{
			await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
		{
		}

		_process.Dispose();
	}
}
