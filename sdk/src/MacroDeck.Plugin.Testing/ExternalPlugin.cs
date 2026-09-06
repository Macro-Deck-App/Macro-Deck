using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Testing;

/// <summary>A plugin built with <see cref="MacroDeckTestHost.LaunchAsync" />: a real, separate process this test host supervises.</summary>
public sealed class ExternalPlugin : PluginUnderTest
{
	private readonly Process _process;
	private readonly MacroDeckTestHost _host;
	private readonly string _pluginId;
	private readonly TempStateDirectory _stateDirectory;
	private readonly StringBuilder _standardOutput = new();
	private readonly StringBuilder _standardError = new();
	private readonly Lock _outputGate = new();

	internal ExternalPlugin(
		Process process,
		Uri baseAddress,
		MacroDeckTestHost host,
		string pluginId,
		TempStateDirectory stateDirectory)
	{
		_process = process;
		BaseAddress = baseAddress;
		_host = host;
		_pluginId = pluginId;
		_stateDirectory = stateDirectory;

		_process.OutputDataReceived += OnOutputReceived;
		_process.ErrorDataReceived += OnErrorReceived;
		_process.BeginOutputReadLine();
		_process.BeginErrorReadLine();
	}

	/// <inheritdoc />
	public override Uri BaseAddress { get; }

	/// <summary>The operating system process id.</summary>
	public int ProcessId => _process.Id;

	/// <summary>Whether the process has exited.</summary>
	public bool HasExited => _process.HasExited;

	/// <summary>The process's exit code, once it has exited. Null while it is still running.</summary>
	public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

	/// <summary>Everything the process has written to standard output so far.</summary>
	public string StandardOutput
	{
		get
		{
			lock (_outputGate)
			{
				return _standardOutput.ToString();
			}
		}
	}

	/// <summary>Everything the process has written to standard error so far.</summary>
	public string StandardError
	{
		get
		{
			lock (_outputGate)
			{
				return _standardError.ToString();
			}
		}
	}

	/// <summary>
	/// Asks the plugin to shut down the way a real supervisor does: <c>session.goodbye</c> over the
	/// current session, then a close with <see cref="ProtocolCloseCodes.SupervisorShutdown" />. Waits up
	/// to <paramref name="grace" /> (default 10 seconds) for the process to exit on its own; if it has
	/// not, kills it and waits once more for the same budget so the report reflects reality either way.
	/// </summary>
	public async Task<GracefulShutdownReport> StopGracefullyAsync(TimeSpan? grace = null,
		CancellationToken cancellationToken = default)
	{
		var budget = grace ?? TimeSpan.FromSeconds(10);
		var stopwatch = Stopwatch.StartNew();

		try
		{
			await _host.SendToPluginAsync(_pluginId,
					new ProtocolEnvelope
					{
						Type = MessageTypes.SessionGoodbye,
						Id = Guid.CreateVersion7().ToString(),
						Payload = JsonSerializer.SerializeToElement(
							new SessionGoodbyePayload { Reason = "The test host is stopping this plugin." },
							PluginProtocolJson.Options)
					})
				.ConfigureAwait(false);
		}
		catch (InvalidOperationException)
		{
			// No live connection to say goodbye on - proceed straight to closing/killing the process.
		}

		try
		{
			await _host.DisconnectPluginAsync(_pluginId, ProtocolCloseCodes.SupervisorShutdown, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (InvalidOperationException)
		{
		}

		var exitedGracefully = await WaitForExitAsync(budget, cancellationToken).ConfigureAwait(false);
		var killed = false;

		if (!exitedGracefully)
		{
			TryKill();
			killed = true;
			await WaitForExitAsync(budget, cancellationToken).ConfigureAwait(false);
		}

		stopwatch.Stop();

		return new GracefulShutdownReport
		{
			ExitedWithinGrace = exitedGracefully,
			Elapsed = stopwatch.Elapsed,
			ExitCode = ExitCode,
			Killed = killed
		};
	}

	/// <inheritdoc />
	public override async ValueTask DisposeAsync()
	{
		if (!_process.HasExited)
		{
			TryKill();

			try
			{
				await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
			{
			}
		}

		_process.OutputDataReceived -= OnOutputReceived;
		_process.ErrorDataReceived -= OnErrorReceived;
		_process.Dispose();
		_stateDirectory.Dispose();
		DisposeHttpClient();
	}

	private void OnOutputReceived(object? sender, DataReceivedEventArgs e)
	{
		if (e.Data is null)
		{
			return;
		}

		lock (_outputGate)
		{
			_standardOutput.AppendLine(e.Data);
		}
	}

	private void OnErrorReceived(object? sender, DataReceivedEventArgs e)
	{
		if (e.Data is null)
		{
			return;
		}

		lock (_outputGate)
		{
			_standardError.AppendLine(e.Data);
		}
	}

	private async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (_process.HasExited)
		{
			return true;
		}

		try
		{
			await _process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken)
				.ConfigureAwait(false);
			return true;
		}
		catch (TimeoutException)
		{
			return _process.HasExited;
		}
	}

	private void TryKill()
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
			// Already exited between the check and the call.
		}
	}
}
