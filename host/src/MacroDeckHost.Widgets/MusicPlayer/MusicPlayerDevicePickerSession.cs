using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.MusicPlayer;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// The live half of the device picker: the devices the player listed, and whether it could list them.
///
/// <para>
/// The picking itself is not here. A row carries its own answer and the client settles the modal with
/// it, so this session never sees the press.
/// </para>
///
/// <para>
/// The list is read once, when the dialog opens. There is no search to debounce and no window to grow -
/// a player has a handful of devices - so the whole session is that one read.
/// </para>
/// </summary>
internal sealed class MusicPlayerDevicePickerSession : IUiSession
{
	private readonly UiView _view;
	private readonly IMusicPlayerRegistry _registry;
	private readonly ILogger _logger;
	private readonly string _instanceId;

	private readonly UiState<MusicPlayerDevicePickerState> _state = new(MusicPlayerDevicePickerState.Loading);

	// The load runs on its own Task.Run thread and writes the state a client's dispatch reads. Taken
	// before the view's own serialization everywhere, so the two are always acquired in that order.
	private readonly Lock _sync = new();

	private CancellationTokenSource? _load;
	private bool _disposed;

	public MusicPlayerDevicePickerSession(
		UiSurface surface,
		IMusicPlayerRegistry registry,
		string instanceId,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(surface);

		_registry = registry;
		_instanceId = instanceId;
		_logger = logger.ForContext<MusicPlayerDevicePickerSession>();

		_view = new UiView(surface, MusicPlayerDevicePickerView.Build(_state));

		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		Load();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		lock (_sync)
		{
			return _view.Tree;
		}
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		lock (_sync)
		{
			return _view.DrainPatches();
		}
	}

	public void Dispatch(UiEvent uiEvent)
	{
		lock (_sync)
		{
			_view.Dispatch(uiEvent);
		}
	}

	public ValueTask DisposeAsync()
	{
		CancellationTokenSource? load;

		// The flag and the source are taken in one step, so a load that is between its own cancellation
		// check and its write cannot slip a patch in behind disposal.
		lock (_sync)
		{
			if (_disposed)
			{
				return ValueTask.CompletedTask;
			}

			_disposed = true;
			load = _load;
			_load = null;
		}

		Cancel(load);

		return ValueTask.CompletedTask;
	}

	/// <summary>Cancels and disposes the source. Only ever reached by the thread that took it out of the
	/// field it lived in, so the cancel can never meet another thread's disposal of the same
	/// source.</summary>
	private static void Cancel(CancellationTokenSource? source)
	{
		if (source is null)
		{
			return;
		}

		source.Cancel();
		source.Dispose();
	}

	private void Load()
	{
		CancellationToken token;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var current = new CancellationTokenSource();
			_load = current;
			token = current.Token;
		}

		_ = Task.Run(async () =>
			{
				try
				{
					var state = await LoadAsync(token).ConfigureAwait(false);

					lock (_sync)
					{
						if (_disposed || token.IsCancellationRequested)
						{
							return;
						}

						_state.Value = state;
					}
				}
				catch (OperationCanceledException)
				{
					// The dialog closed. Not a failure to report.
				}
			},
			token);
	}

	private async Task<MusicPlayerDevicePickerState> LoadAsync(CancellationToken cancellationToken)
	{
		if (_registry.GetPlayer(_instanceId) is not IMusicPlayerDeviceProvider devices)
		{
			return MusicPlayerDevicePickerState.Unsupported;
		}

		try
		{
			var listed = await devices.GetDevicesAsync(cancellationToken).ConfigureAwait(false);

			return new MusicPlayerDevicePickerState { Devices = listed, Available = true, Supported = true };
		}
		catch (OperationCanceledException)
		{
			throw;
		}
#pragma warning disable CA1031 // A player is plugin-owned; its fault leaves a readable dialog, not a dead session.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception,
				"Failed to read the devices for music player instance {InstanceId}",
				_instanceId);

			// An empty list means "this player has no devices" only when the read succeeded. Collapsing
			// the two is what would make an unreachable player render as "no devices available".
			return MusicPlayerDevicePickerState.Unavailable;
		}
	}
}
