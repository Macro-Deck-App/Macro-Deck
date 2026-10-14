using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Sdk.Batteries;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteBatteryProviderContext : IBatteryProviderContext, IDisposable
{
	private readonly IHostInvoker _invoker;
	private readonly PluginConnectionState _state;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, BatteryDevice> _published = new(StringComparer.Ordinal);
	private bool _unsynced;
	private object? _unsupportedOn;
	private int _unsupportedLogged;

	public RemoteBatteryProviderContext(IHostInvoker invoker, PluginConnectionState state, ILogger logger)
	{
		_invoker = invoker;
		_state = state;
		_logger = logger.ForContext<RemoteBatteryProviderContext>();
		_state.Connected += OnConnected;
	}

	internal TimeSpan RateLimitRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

	public Task PublishAsync(BatteryDevice device, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);
		return PublishAsync([device], cancellationToken);
	}

	public async Task PublishAsync(IReadOnlyList<BatteryDevice> devices, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(devices);

		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var device in devices)
		{
			if (device is null)
			{
				throw new ArgumentException("A battery device list cannot contain null.", nameof(devices));
			}

			if (!ids.Add(device.Id))
			{
				throw new ArgumentException($"The battery device '{device.Id}' is listed twice.", nameof(devices));
			}
		}

		if (devices.Count == 0)
		{
			return;
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var total = _published.Count + ids.Count(id => !_published.ContainsKey(id));
			if (total > BatteryLimits.MaxDevicesPerProvider)
			{
				throw new InvalidOperationException(
					$"A battery provider can publish at most {BatteryLimits.MaxDevicesPerProvider} devices.");
			}

			var delivered = await SendAsync(HostOperations.Batteries.Publish,
					new BatteriesPublishArguments { Devices = [.. devices.Select(BatteryWire.ToDto)] },
					cancellationToken)
				.ConfigureAwait(false);

			foreach (var device in devices)
			{
				_published[device.Id] = device;
			}

			_unsynced |= !delivered;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var delivered = await SendAsync(HostOperations.Batteries.Remove,
					new BatteriesRemoveArguments { DeviceIds = [deviceId] },
					cancellationToken)
				.ConfigureAwait(false);

			_published.Remove(deviceId);
			_unsynced |= !delivered;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task ResetAsync()
	{
		await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
		try
		{
			if (_published.Count > 0 || _unsynced)
			{
				await WithdrawAsync().ConfigureAwait(false);
			}

			_published.Clear();
			_unsynced = false;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		_state.Connected -= OnConnected;
		_gate.Dispose();
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e)
	{
		if (e.Resumed)
		{
			_ = SyncAsync();
		}
	}

	private async Task SyncAsync()
	{
		try
		{
			await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
			try
			{
				if (_published.Count == 0 && !_unsynced)
				{
					return;
				}

				var delivered = await SendAsync(HostOperations.Batteries.Sync,
						new BatteriesSyncArguments { Devices = [.. _published.Values.Select(BatteryWire.ToDto)] },
						CancellationToken.None)
					.ConfigureAwait(false);
				_unsynced = !delivered;
			}
			finally
			{
				_gate.Release();
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Resending the published battery devices failed");
		}
	}

	// Best effort: a host without the withdraw operation, or without battery support at all, keeps
	// the devices until the session ends, which is what it did before the operation existed.
	private async Task WithdrawAsync()
	{
		var connection = _state.ActiveConnection;
		if (connection is null || ReferenceEquals(_unsupportedOn, connection))
		{
			return;
		}

		try
		{
			await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Batteries,
					HostOperations.Batteries.Withdraw,
					null,
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Withdrawing the published battery devices failed");
		}
	}

	private async Task<bool> SendAsync(string operation, object arguments, CancellationToken cancellationToken)
	{
		var connection = _state.ActiveConnection;
		if (connection is null)
		{
			return false;
		}

		if (ReferenceEquals(_unsupportedOn, connection))
		{
			return true;
		}

		for (var attempt = 0;; attempt++)
		{
			try
			{
				await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Batteries,
						operation,
						arguments,
						cancellationToken)
					.ConfigureAwait(false);
				return true;
			}
			catch (HostInvocationException exception) when (IsCode(exception, ProtocolErrorCodes.CapabilityUnsupported))
			{
				_unsupportedOn = connection;
				if (Interlocked.Exchange(ref _unsupportedLogged, 1) == 0)
				{
					_logger.Information(
						"This Macro Deck does not support battery providers, so published battery devices are not shown");
				}

				return true;
			}
			catch (HostInvocationException exception) when (attempt == 0
				&& IsCode(exception, ProtocolErrorCodes.RateLimited))
			{
				await Task.Delay(RateLimitRetryDelay, cancellationToken).ConfigureAwait(false);
			}
			catch (HostInvocationException) when (!ReferenceEquals(_state.ActiveConnection, connection))
			{
				return false;
			}
		}
	}

	private static bool IsCode(HostInvocationException exception, string code)
	{
		return string.Equals(exception.Code, code, StringComparison.Ordinal);
	}
}
