using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

internal sealed class PluginProcess : IPluginProcess
{
	private readonly Process _process;
	private readonly IPluginProcessJob _job;
	private readonly ILogger _logger;

	private readonly TaskCompletionSource<int> _exitedSource =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly BootstrapOutputBuffer _output;

	private int _exitCode;
	private volatile bool _hasExited;
	private int _disposed;

	public PluginProcess(Process process,
		IPluginProcessJob job,
		int bootstrapOutputMaxLines,
		int bootstrapOutputMaxBytes,
		ILogger logger)
	{
		_process = process;
		_job = job;
		_logger = logger.ForContext<PluginProcess>();
		_output = new BootstrapOutputBuffer(bootstrapOutputMaxLines, bootstrapOutputMaxBytes);
		Id = process.Id;
		StartedAt = ReadStartTime(process);

		_process.EnableRaisingEvents = true;
		_process.OutputDataReceived += OnOutputDataReceived;
		_process.ErrorDataReceived += OnErrorDataReceived;
		_process.Exited += OnExited;

		_process.BeginOutputReadLine();
		_process.BeginErrorReadLine();
	}

	public int Id { get; }

	public DateTimeOffset StartedAt { get; }

	public bool HasExited => _hasExited;

	public int? ExitCode => _hasExited ? _exitCode : null;

	public Task<int> Exited => _exitedSource.Task;

	public IReadOnlyList<string> BootstrapOutput => _output.Snapshot();

	private void OnOutputDataReceived(object? sender, DataReceivedEventArgs e)
	{
		if (e.Data is not null)
		{
			_output.Append(e.Data);
		}
	}

	private void OnErrorDataReceived(object? sender, DataReceivedEventArgs e)
	{
		if (e.Data is not null)
		{
			_output.Append(e.Data);
		}
	}

	private void OnExited(object? sender, EventArgs e)
	{
		try
		{
			_exitCode = _process.ExitCode;
		}
		catch (InvalidOperationException)
		{
			_exitCode = -1;
		}

		_hasExited = true;
		_exitedSource.TrySetResult(_exitCode);
	}

	public Task KillTree(CancellationToken cancellationToken = default)
	{
		if (!_job.TryTerminate())
		{
			ProcessTreeTermination.KillTree(_process, Id, _logger);
		}

		return Task.CompletedTask;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_process.OutputDataReceived -= OnOutputDataReceived;
		_process.ErrorDataReceived -= OnErrorDataReceived;
		_process.Exited -= OnExited;
		_process.Dispose();
		_job.Dispose();
	}

	private static DateTimeOffset ReadStartTime(Process process)
	{
		try
		{
			return process.StartTime.ToUniversalTime();
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
		{
			return DateTimeOffset.UtcNow;
		}
	}

	private sealed class BootstrapOutputBuffer
	{
		private readonly object _lock = new();
		private readonly Queue<string> _lines = new();
		private readonly int _maxLines;
		private readonly int _maxBytes;
		private int _totalBytes;

		public BootstrapOutputBuffer(int maxLines, int maxBytes)
		{
			_maxLines = Math.Max(1, maxLines);
			_maxBytes = Math.Max(1, maxBytes);
		}

		public void Append(string line)
		{
			var size = Encoding.UTF8.GetByteCount(line);

			lock (_lock)
			{
				_lines.Enqueue(line);
				_totalBytes += size;

				while ((_lines.Count > _maxLines || _totalBytes > _maxBytes) && _lines.Count > 0)
				{
					var removed = _lines.Dequeue();
					_totalBytes -= Encoding.UTF8.GetByteCount(removed);
				}
			}
		}

		public string[] Snapshot()
		{
			lock (_lock)
			{
				return _lines.ToArray();
			}
		}
	}
}
