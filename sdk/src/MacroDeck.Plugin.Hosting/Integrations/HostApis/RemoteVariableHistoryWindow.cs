using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteVariableHistoryWindow : IVariableHistoryWindow
{
	internal static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(1);

	private readonly IHostInvoker _invoker;
	private readonly string _windowId;
	private readonly ITimer _timer;
	private readonly Lock _gate = new();
	private IReadOnlyList<double?> _values = [];
	private int _reading;
	private bool _disposed;

	public RemoteVariableHistoryWindow(IHostInvoker invoker, string windowId, TimeProvider time)
	{
		_invoker = invoker;
		_windowId = windowId;
		_timer = time.CreateTimer(_ => _ = ReadAsync(), null, TimeSpan.Zero, ReadInterval);
	}

	public event EventHandler? Changed;

	public IReadOnlyList<double?> Values
	{
		get
		{
			lock (_gate)
			{
				return _values;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		await _timer.DisposeAsync().ConfigureAwait(false);

		try
		{
			await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
					HostOperations.Variables.HistoryClose,
					new VariablesHistoryWindowArguments { WindowId = _windowId },
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// The host forgets a connection's windows when it ends, so a close that cannot be delivered
			// leaves nothing behind.
		}
	}

	private async Task ReadAsync()
	{
		if (Interlocked.Exchange(ref _reading, 1) == 1)
		{
			return;
		}

		try
		{
			var result = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
					HostOperations.Variables.HistoryRead,
					new VariablesHistoryWindowArguments { WindowId = _windowId },
					CancellationToken.None)
				.ConfigureAwait(false);

			if (result?.Deserialize<VariablesHistoryReadResult>(PluginProtocolJson.Options) is not { } read)
			{
				return;
			}

			lock (_gate)
			{
				if (_disposed || _values.SequenceEqual(read.Values))
				{
					return;
				}

				_values = [.. read.Values];
			}

			Changed?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
		}
		finally
		{
			Volatile.Write(ref _reading, 0);
		}
	}
}
