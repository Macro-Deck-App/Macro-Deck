using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using System.Text.Json;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.MusicPlayer;

/// <summary>
/// The runtime pickers a music player action opens on the client that ran it.
///
/// <para>
/// Both are Macro Deck UI dialogs: the host registers a modal, the client opens the dialog session
/// itself and renders the tree the picker provider builds, and every row answers with the item's or the
/// device's own id. Any client that renders the widget profile therefore shows them - which the pickers
/// they replaced could not manage, because each dialog was written once per client framework on top of a
/// push, a listing endpoint and a submit of its own.
/// </para>
///
/// <para>
/// Reading back an id the host issued is also what keeps the rest of each pick off the wire. The device
/// picker it replaced had the client echo the instance and whether to start playback back to the host,
/// which meant any client in the origin group could name a transfer the action never asked for; the
/// action knows both, and now keeps them.
/// </para>
/// </summary>
public sealed class ActionInteractions : IActionInteractions
{
	/// <summary>The view the music player integration's picker provider serves. Spelled here rather than
	/// referenced: the widgets project sits above this one.</summary>
	private const string PickViewId = "music-player-pick";

	/// <summary>The view the device picker provider serves, spelled here for the same reason.</summary>
	private const string DevicePickViewId = "music-player-device-pick";

	private const string InstanceIdKey = "instanceId";
	private const string KindKey = "kind";

	/// <summary>Whose provider serves the picker dialog. Spelled here for the same reason the view id is:
	/// the project that owns the provider sits above this one.</summary>
	private const string PickerIntegrationId = "app.macro-deck.music-player";

	/// <summary>
	/// Longer than a user takes to pick, and short enough that a dialog nobody answered does not hold a
	/// registration for the life of the process. A modal the client never opened is swept anyway; this
	/// bounds the one it opened and walked away from.
	/// </summary>
	private static readonly TimeSpan _pickTimeout = TimeSpan.FromMinutes(10);

	private readonly IUiTransport _transport;
	private readonly IModalInteractionCoordinator _coordinator;
	private readonly IMusicPlayerRegistry _registry;
	private readonly ILogger _logger;

	public ActionInteractions(
		IUiTransport transport,
		IModalInteractionCoordinator coordinator,
		IMusicPlayerRegistry registry,
		ILogger logger)
	{
		_transport = transport;
		_coordinator = coordinator;
		_registry = registry;
		_logger = logger.ForContext<ActionInteractions>();
	}

	public void RequestItemPicker(string? originClientId,
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? prompt = null)
	{
		if (string.IsNullOrEmpty(originClientId) || string.IsNullOrEmpty(instanceId))
		{
			return;
		}

		// Fire and forget, as the contract says: the action has already returned by the time the user
		// answers, and the playback is this pick's own business rather than the flow's.
		_ = Task.Run(() => PickAndPlayAsync(originClientId, instanceId, kind));
	}

	public void RequestDevicePicker(string? originClientId,
		string instanceId,
		bool startPlayback,
		string? prompt = null)
	{
		if (string.IsNullOrEmpty(originClientId) || string.IsNullOrEmpty(instanceId))
		{
			return;
		}

		// Fire and forget, as the contract says: the action has already returned by the time the user
		// answers, and the transfer is this pick's own business rather than the flow's.
		_ = Task.Run(() => PickAndTransferAsync(originClientId, instanceId, startPlayback));
	}

	private async Task PickAndPlayAsync(
		string originClientId,
		string instanceId,
		MusicPlayerCatalogItemKind kind)
	{
		using var timeout = new CancellationTokenSource(_pickTimeout);

		try
		{
			var modal = new ModalDefinition
			{
				ViewId = PickViewId,
				Title = kind == MusicPlayerCatalogItemKind.Playlist
					? AppStrings.Dialogs.ItemPicker.PickAPlaylist()
					: AppStrings.Dialogs.ItemPicker.PickATrack(),
				Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[InstanceIdKey] = JsonSerializer.SerializeToElement(instanceId),
					[KindKey] = JsonSerializer.SerializeToElement(kind.ToString()),
				},
			};

			var modalId = _coordinator.Register(PickerIntegrationId, originClientId, modal);
			if (modalId is null)
			{
				return;
			}

			await _transport.SendToGroup(UiClientGroups.For(originClientId),
					new UiModalOpenedEvent { ModalId = modalId, Title = modal.Title ?? default },
					timeout.Token)
				.ConfigureAwait(false);

			var outcome = await _coordinator.AwaitAsync(modalId, timeout.Token).ConfigureAwait(false);
			if (outcome.Cancelled || outcome.Value.ValueKind != JsonValueKind.String)
			{
				_logger.Debug("The {Kind} picker for {InstanceId} closed without a choice", kind, instanceId);

				return;
			}

			await PlayAsync(instanceId, outcome.Value.GetString(), kind, timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Nobody answered in time, or the host is going down. Neither is a failure to report.
		}
#pragma warning disable CA1031 // Nothing awaits this; an escaping exception would be an unobserved fault.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Error(exception, "The item picker failed for music player instance {InstanceId}", instanceId);
		}
	}

	private async Task PickAndTransferAsync(string originClientId, string instanceId, bool startPlayback)
	{
		using var timeout = new CancellationTokenSource(_pickTimeout);

		try
		{
			var modal = new ModalDefinition
			{
				ViewId = DevicePickViewId,
				Title = startPlayback
					? AppStrings.Dialogs.DevicePicker.PlayOnDevice()
					: AppStrings.Dialogs.DevicePicker.TransferPlaybackTo(),
				Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[InstanceIdKey] = JsonSerializer.SerializeToElement(instanceId),
				},
			};

			var modalId = _coordinator.Register(PickerIntegrationId, originClientId, modal);
			if (modalId is null)
			{
				return;
			}

			await _transport.SendToGroup(UiClientGroups.For(originClientId),
					new UiModalOpenedEvent { ModalId = modalId, Title = modal.Title ?? default },
					timeout.Token)
				.ConfigureAwait(false);

			var outcome = await _coordinator.AwaitAsync(modalId, timeout.Token).ConfigureAwait(false);
			if (outcome.Cancelled || outcome.Value.ValueKind != JsonValueKind.String)
			{
				_logger.Debug("The device picker for {InstanceId} closed without a choice", instanceId);

				return;
			}

			await TransferAsync(instanceId, outcome.Value.GetString(), startPlayback, timeout.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Nobody answered in time, or the host is going down. Neither is a failure to report.
		}
#pragma warning disable CA1031 // Nothing awaits this; an escaping exception would be an unobserved fault.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Error(exception, "The device picker failed for music player instance {InstanceId}", instanceId);
		}
	}

	private async Task TransferAsync(
		string instanceId,
		string? deviceId,
		bool startPlayback,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(deviceId))
		{
			return;
		}

		if (_registry.GetPlayer(instanceId) is not IMusicPlayerDeviceProvider devices)
		{
			_logger.Warning("Music player instance {InstanceId} cannot switch playback devices", instanceId);

			return;
		}

		_logger.Debug("Transferring playback to picked device {DeviceId} on {InstanceId}", deviceId, instanceId);

		// The id is one this host issued into the dialog and read back, so it names a device the player
		// listed rather than one a client asked for.
		await devices.TransferPlaybackAsync(deviceId.Trim(), startPlayback, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task PlayAsync(
		string instanceId,
		string? itemId,
		MusicPlayerCatalogItemKind kind,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(itemId))
		{
			return;
		}

		if (_registry.GetPlayer(instanceId) is not ICatalogMusicPlayer player)
		{
			_logger.Warning("Music player instance {InstanceId} cannot play catalog items", instanceId);

			return;
		}

		_logger.Debug("Playing picked {Kind} {ItemId} on {InstanceId}", kind, itemId, instanceId);

		// The id is one this host issued into the dialog and read back, so it names an item the library
		// listed rather than one a client asked for.
		var trimmed = itemId.Trim();
		await player.PlayItemAsync(new MusicPlayerCatalogItem(trimmed, trimmed, kind), cancellationToken)
			.ConfigureAwait(false);
	}
}
