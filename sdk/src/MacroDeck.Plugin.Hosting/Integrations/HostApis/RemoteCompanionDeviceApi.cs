using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Companion;
using ILogger = Serilog.ILogger;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteCompanionDeviceApi : ICompanionDeviceApi, IDisposable
{
	// The host's own windows plus room for the round trip, so the host answers before the plugin gives up.
	private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan _sendTimeout = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(150);
	private static readonly TimeSpan _pushAfterReadyTimeout = TimeSpan.FromSeconds(5);

	private readonly IHostInvoker _invoker;
	private readonly HostStateCache _stateCache;
	private readonly IPluginHostAssetReceiver? _assets;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private CompanionDeviceAccess _access = CompanionDeviceAccess.Unsupported;
	private IReadOnlyList<CompanionDevice> _devices = [];
	private Task _raising = Task.CompletedTask;

	public RemoteCompanionDeviceApi(
		IHostInvoker invoker,
		HostStateCache stateCache,
		ILogger logger,
		IPluginHostAssetReceiver? assets = null)
	{
		_invoker = invoker;
		_stateCache = stateCache;
		_assets = assets;
		_logger = logger.ForContext<RemoteCompanionDeviceApi>();
		_stateCache.CompanionDevicesChanged += OnStateChanged;
		Update();
	}

	public event EventHandler? AccessChanged;

	public event EventHandler<CompanionDeviceEventArgs>? DeviceReady;

	public event EventHandler<CompanionDeviceEventArgs>? DeviceChanged;

	private event Action? Applied;

	public CompanionDeviceAccess Access
	{
		get
		{
			lock (_gate)
			{
				return _access;
			}
		}
	}

	public IReadOnlyList<CompanionDevice> Devices
	{
		get
		{
			lock (_gate)
			{
				return _devices;
			}
		}
	}

	public CompanionDevice? FindDevice(Guid deviceId)
	{
		lock (_gate)
		{
			return _devices.FirstOrDefault(device => device.Id == deviceId);
		}
	}

	public void Dispose()
	{
		_stateCache.CompanionDevicesChanged -= OnStateChanged;
	}

	public async Task<bool> WaitForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		if (FindDevice(deviceId) is not null)
		{
			return true;
		}

		if (Access == CompanionDeviceAccess.Unsupported)
		{
			return false;
		}

		var result = await InvokeAsync<CompanionDeviceWaitResultDto>(HostOperations.CompanionDevices.Wait,
			new CompanionDeviceArguments { DeviceId = deviceId.ToString("D") },
			_waitTimeout,
			cancellationToken);
		if (result?.Ready != true)
		{
			return false;
		}

		await WaitForPushAsync(deviceId, cancellationToken);
		return true;
	}

	public async Task<bool> SendAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		if (Access == CompanionDeviceAccess.Unsupported)
		{
			return false;
		}

		var result = await InvokeAsync<CompanionDeviceSendResultDto>(HostOperations.CompanionDevices.Send,
			CompanionDeviceWire.ToArguments(deviceId, command),
			_sendTimeout,
			cancellationToken);
		return result?.Sent == true;
	}

	public async Task<CompanionDeviceCommandResult> RequestAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		if (Access == CompanionDeviceAccess.Unsupported)
		{
			return CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.NotConnected);
		}

		try
		{
			var result = await InvokeOrThrowAsync<CompanionDeviceRequestResultDto>(
				HostOperations.CompanionDevices.Request,
				CompanionDeviceWire.ToArguments(deviceId, command),
				_requestTimeout,
				cancellationToken);
			return result is null
				? CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.Failed)
				: CompanionDeviceWire.ToResult(result);
		}
		catch (HostInvocationException exception)
		{
			return CompanionDeviceCommandResult.Failed(ToFailure(exception));
		}
	}

	public async Task<CompanionScreenshotResult> CaptureScreenshotAsync(
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken = default)
	{
		if (Access == CompanionDeviceAccess.Unsupported)
		{
			return CompanionScreenshotResult.Failed(CompanionDeviceFailure.NotConnected);
		}

		if (_assets is null)
		{
			return CompanionScreenshotResult.Failed(CompanionDeviceFailure.Failed);
		}

		CompanionScreenshotResultDto? result;
		try
		{
			result = await InvokeOrThrowAsync<CompanionScreenshotResultDto>(HostOperations.CompanionDevices.Screenshot,
				new CompanionScreenshotArguments
				{
					DeviceId = deviceId.ToString("D"),
					Mode = CompanionDeviceWire.ToWire(mode)
				},
				_requestTimeout,
				cancellationToken);
		}
		catch (HostInvocationException exception)
		{
			return CompanionScreenshotResult.Failed(ToFailure(exception));
		}

		if (result is null)
		{
			return CompanionScreenshotResult.Failed(CompanionDeviceFailure.Failed);
		}

		if (result.Failure is not null || string.IsNullOrEmpty(result.TransferId))
		{
			return CompanionScreenshotResult.Failed(CompanionDeviceWire.ToFailure(result.Failure));
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(ProtocolTimeouts.AssetUpload);
		try
		{
			var transfer = await _assets.AwaitAsync(result.TransferId, timeout.Token).ConfigureAwait(false);
			return transfer.Accepted
				? CompanionScreenshotResult.Of(transfer.Content)
				: CompanionScreenshotResult.Failed(CompanionDeviceFailure.Failed);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return CompanionScreenshotResult.Failed(CompanionDeviceFailure.TimedOut);
		}
	}

	private async Task<T?> InvokeAsync<T>(
		string operation,
		object arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
		where T : class
	{
		try
		{
			return await InvokeOrThrowAsync<T>(operation, arguments, timeout, cancellationToken);
		}
		catch (HostInvocationException exception)
		{
			_logger.Debug(exception, "The host refused companion-devices/{Operation}", operation);
			return null;
		}
	}

	private async Task<T?> InvokeOrThrowAsync<T>(
		string operation,
		object arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
		where T : class
	{
		var data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.CompanionDevices,
				operation,
				arguments,
				timeout,
				cancellationToken)
			.ConfigureAwait(false);
		try
		{
			return data?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static CompanionDeviceFailure ToFailure(HostInvocationException exception)
	{
		return exception.Code switch
		{
			ProtocolErrorCodes.CompanionDevicesNotAllowed => CompanionDeviceFailure.NotAllowed,
			ProtocolErrorCodes.CapabilityUnsupported => CompanionDeviceFailure.NotConnected,
			ProtocolErrorCodes.Timeout => CompanionDeviceFailure.TimedOut,
			_ => CompanionDeviceFailure.Failed
		};
	}

	// The host answers ready before its next push reaches this plugin, so a caller reading FindDevice right after
	// would otherwise still see nothing.
	private async Task WaitForPushAsync(Guid deviceId, CancellationToken cancellationToken)
	{
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnApplied()
		{
			if (FindDevice(deviceId) is not null)
			{
				arrived.TrySetResult();
			}
		}

		Applied += OnApplied;
		try
		{
			OnApplied();
			await arrived.Task.WaitAsync(_pushAfterReadyTimeout, cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
		}
		finally
		{
			Applied -= OnApplied;
		}
	}

	private void OnStateChanged()
	{
		var changes = Update();
		Applied?.Invoke();
		if (changes.Count == 0)
		{
			return;
		}

		// Raised off the receive loop and in push order, so a slow or throwing handler cannot stall the connection.
		lock (_gate)
		{
			_raising = _raising.ContinueWith(_ => Raise(changes),
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
	}

	private List<Change> Update()
	{
		var state = _stateCache.Get<CompanionDevicesStateDto>(Protocol.Callbacks.HostApis.CompanionDevices);
		var access = state is null
			? CompanionDeviceAccess.Unsupported
			: state.Access == CompanionDeviceAccessStates.Available
				? CompanionDeviceAccess.Available
				: CompanionDeviceAccess.NotAllowed;
		var pushed = access == CompanionDeviceAccess.Available
			? state!.Devices.Where(dto => dto is not null)
				.Select(CompanionDeviceWire.ToDevice)
				.OfType<CompanionDevice>()
				.DistinctBy(device => device.Id)
				.ToList()
			: [];

		var changes = new List<Change>();
		lock (_gate)
		{
			if (access != _access)
			{
				_access = access;
				changes.Add(new Change(ChangeKind.Access, Guid.Empty, null));
			}

			var before = _devices.ToDictionary(device => device.Id);
			foreach (var device in pushed)
			{
				if (!before.Remove(device.Id, out var previous) || previous.ReadySequence != device.ReadySequence)
				{
					changes.Add(new Change(ChangeKind.Changed, device.Id, device));
					changes.Add(new Change(ChangeKind.Ready, device.Id, device));
				}
				else if (previous != device && !SameState(previous.State, device.State))
				{
					changes.Add(new Change(ChangeKind.Changed, device.Id, device));
				}
			}

			changes.AddRange(before.Keys.Select(id => new Change(ChangeKind.Changed, id, null)));
			_devices = pushed;
		}

		return changes;
	}

	private static bool SameState(CompanionDeviceState left, CompanionDeviceState right)
	{
		return left with { Capabilities = right.Capabilities, RequestableCapabilities = right.RequestableCapabilities }
			== right
			&& left.Capabilities.SetEquals(right.Capabilities)
			&& left.RequestableCapabilities.SetEquals(right.RequestableCapabilities);
	}

	private void Raise(List<Change> changes)
	{
		foreach (var change in changes)
		{
			try
			{
				switch (change.Kind)
				{
					case ChangeKind.Access:
						AccessChanged?.Invoke(this, EventArgs.Empty);
						break;
					case ChangeKind.Ready:
						DeviceReady?.Invoke(this, new CompanionDeviceEventArgs(change.DeviceId, change.Device));
						break;
					case ChangeKind.Changed:
						DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(change.DeviceId, change.Device));
						break;
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A Companion device {Change} handler failed", change.Kind);
			}
		}
	}

	private enum ChangeKind
	{
		Access,
		Ready,
		Changed
	}

	private sealed record Change(ChangeKind Kind, Guid DeviceId, CompanionDevice? Device);
}
