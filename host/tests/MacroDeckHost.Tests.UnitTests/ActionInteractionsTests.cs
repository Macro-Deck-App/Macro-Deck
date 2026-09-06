using System.Text.Json;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using MacroDeckHost.Tests.UnitTests.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests;

/// <summary>
/// Both pickers are Macro Deck UI dialogs, so what each one reports is a modal the client is asked to
/// open - never a push carrying a picker of its own.
/// </summary>
[TestFixture]
internal sealed class ActionInteractionsTests
{
	private static ActionInteractions Build(
		RecordingUiTransport transport,
		RecordingModalCoordinator coordinator,
		IMusicPlayerRegistry? registry = null)
		=> new(transport, coordinator, registry ?? new SinglePlayerRegistry(null), Serilog.Log.Logger);

	private static async Task<bool> Settles(Func<bool> condition)
	{
		// The pick runs detached, as the contract says it must: nothing awaits it, so the assertion has
		// to wait for it rather than the other way round.
		for (var attempt = 0; attempt < 100; attempt++)
		{
			if (condition())
			{
				return true;
			}

			await Task.Delay(10).ConfigureAwait(false);
		}

		return condition();
	}

	[Test]
	public async Task RequestItemPicker_OpensAPickerDialogOnTheOriginClient()
	{
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator();
		var interactions = Build(transport, coordinator);

		interactions.RequestItemPicker("client-abc", "inst-1", MusicPlayerCatalogItemKind.Playlist);

		Assert.That(await Settles(() => transport.GroupMessages.Count > 0), Is.True);

		var sent = transport.GroupMessages.Single();
		Assert.Multiple(() =>
		{
			Assert.That(sent.Group, Is.EqualTo(UiClientGroups.For("client-abc")));
			Assert.That(sent.Message, Is.InstanceOf<UiModalOpenedEvent>());
			Assert.That(((UiModalOpenedEvent)sent.Message).ModalId, Is.EqualTo(coordinator.LastModalId));

			var registered = coordinator.Registered.Single();
			Assert.That(registered.Modal.ViewId, Is.EqualTo("music-player-pick"));
			Assert.That(registered.OriginClientId, Is.EqualTo("client-abc"));
			// The instance and the kind reach the provider as the dialog's own data - it serves one
			// library at a time and has nothing else to learn them from.
			Assert.That(registered.Modal.Data!["instanceId"].GetString(), Is.EqualTo("inst-1"));
			Assert.That(registered.Modal.Data!["kind"].GetString(), Is.EqualTo("Playlist"));
		});
	}

	[Test]
	public async Task RequestItemPicker_WithEmptyClientOrInstance_DoesNothing()
	{
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator();
		var interactions = Build(transport, coordinator);

		interactions.RequestItemPicker(null, "inst-1", MusicPlayerCatalogItemKind.Track);
		interactions.RequestItemPicker("client", "", MusicPlayerCatalogItemKind.Track);

		await Task.Delay(50).ConfigureAwait(false);
		Assert.Multiple(() =>
		{
			Assert.That(transport.GroupMessages, Is.Empty);
			Assert.That(coordinator.Registered, Is.Empty);
		});
	}

	[Test]
	public async Task RequestDevicePicker_OpensADeviceDialogOnTheOriginClient()
	{
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator();
		var interactions = Build(transport, coordinator);

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback: true, "Pick a device");

		Assert.That(await Settles(() => transport.GroupMessages.Count > 0), Is.True);

		var sent = transport.GroupMessages.Single();
		Assert.Multiple(() =>
		{
			Assert.That(sent.Group, Is.EqualTo(UiClientGroups.For("client-abc")));
			// A modal the client is asked to open, not a picker pushed to it. Any client that renders the
			// widget profile can show this; the push it replaced needed a dialog per client framework.
			Assert.That(sent.Message, Is.InstanceOf<UiModalOpenedEvent>());
			Assert.That(((UiModalOpenedEvent)sent.Message).ModalId, Is.EqualTo(coordinator.LastModalId));

			var registered = coordinator.Registered.Single();
			Assert.That(registered.Modal.ViewId, Is.EqualTo("music-player-device-pick"));
			Assert.That(registered.OriginClientId, Is.EqualTo("client-abc"));
			Assert.That(registered.Modal.Data!["instanceId"].GetString(), Is.EqualTo("inst-1"));
		});
	}

	/// <summary>
	/// Whether to start playback is the action's own parameter and stays on the host. The picker this
	/// replaced sent it to the client and transferred playback according to whatever came back.
	/// </summary>
	[Test]
	public async Task RequestDevicePicker_DoesNotPutStartPlaybackOnTheWire()
	{
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator();
		var interactions = Build(transport, coordinator);

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback: true);

		Assert.That(await Settles(() => coordinator.Registered.Count > 0), Is.True);
		Assert.That(coordinator.Registered.Single().Modal.Data!.Keys, Is.EquivalentTo(new[] { "instanceId" }));
	}

	[Test]
	public async Task RequestDevicePicker_WithEmptyClientOrInstance_DoesNothing()
	{
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator();
		var interactions = Build(transport, coordinator);

		interactions.RequestDevicePicker(null, "inst-1", startPlayback: true);
		interactions.RequestDevicePicker("client", "", startPlayback: true);

		await Task.Delay(50).ConfigureAwait(false);
		Assert.Multiple(() =>
		{
			Assert.That(transport.GroupMessages, Is.Empty);
			Assert.That(coordinator.Registered, Is.Empty);
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task RequestDevicePicker_TransfersPlaybackToThePickedDevice(bool startPlayback)
	{
		var player = new FakeDevicePlayer();
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator { Answer = "device-9" };
		var interactions = Build(transport, coordinator, new SinglePlayerRegistry(player));

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback);

		Assert.That(await Settles(() => player.Transferred is not null), Is.True);
		Assert.That(player.Transferred, Is.EqualTo(("device-9", startPlayback)));
	}

	[Test]
	public async Task RequestDevicePicker_TrimsThePickedDeviceId()
	{
		var player = new FakeDevicePlayer();
		var coordinator = new RecordingModalCoordinator { Answer = "  device-9  " };
		var interactions = Build(new RecordingUiTransport(), coordinator, new SinglePlayerRegistry(player));

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback: false);

		Assert.That(await Settles(() => player.Transferred is not null), Is.True);
		Assert.That(player.Transferred!.Value.DeviceId, Is.EqualTo("device-9"));
	}

	[Test]
	public async Task RequestDevicePicker_WhenTheDialogIsClosedWithoutAChoice_TransfersNothing()
	{
		var player = new FakeDevicePlayer();
		var transport = new RecordingUiTransport();
		// The coordinator answers with a cancellation, which is what dismissing, disconnecting and
		// timing out all resolve to.
		var coordinator = new RecordingModalCoordinator();
		var interactions = Build(transport, coordinator, new SinglePlayerRegistry(player));

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback: true);

		Assert.That(await Settles(() => transport.GroupMessages.Count > 0), Is.True);
		await Task.Delay(50).ConfigureAwait(false);
		Assert.That(player.Transferred, Is.Null);
	}

	[Test]
	public async Task RequestDevicePicker_OnAPlayerThatCannotSwitchDevices_TransfersNothingAndDoesNotThrow()
	{
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator { Answer = "device-9" };
		var interactions = Build(transport, coordinator, new SinglePlayerRegistry(new FakeMusicPlayer()));

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback: true);

		Assert.That(await Settles(() => transport.GroupMessages.Count > 0), Is.True);
		await Task.Delay(50).ConfigureAwait(false);
		Assert.Pass();
	}

	/// <summary>A transfer that fails is the player's business, not the flow's: the pick is detached, so
	/// an escaping exception would be an unobserved fault rather than something a user sees.</summary>
	[Test]
	public async Task RequestDevicePicker_WhenTheTransferFails_DoesNotFault()
	{
		var player = new FakeDevicePlayer { TransferFailure = new InvalidOperationException("no route") };
		var transport = new RecordingUiTransport();
		var coordinator = new RecordingModalCoordinator { Answer = "device-9" };
		var interactions = Build(transport, coordinator, new SinglePlayerRegistry(player));

		interactions.RequestDevicePicker("client-abc", "inst-1", startPlayback: true);

		Assert.That(await Settles(() => transport.GroupMessages.Count > 0), Is.True);
		await Task.Delay(50).ConfigureAwait(false);
		Assert.Pass();
	}
}

internal sealed class RecordingUiTransport : IUiTransport
{
	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
		=> Task.CompletedTask;

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public List<(string Group, object Message)> GroupMessages { get; } = new();

	public List<object> Broadcasts { get; } = new();

	public bool FailSends { get; set; }

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
	{
		if (FailSends)
		{
			throw new InvalidOperationException("transport unavailable");
		}

		Broadcasts.Add(message);

		return Task.CompletedTask;
	}

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		GroupMessages.Add((group, message));

		return Task.CompletedTask;
	}
}

internal sealed class RecordingModalCoordinator : IModalInteractionCoordinator
{
	public List<(string IntegrationId, string? OriginClientId, ModalDefinition Modal)> Registered { get; } = [];

	public string? LastModalId { get; private set; }

	/// <summary>What the user picked. Absent means the dialog closed without a choice.</summary>
	public string? Answer { get; init; }

	public string? Register(string integrationId, string? originClientId, ModalDefinition modal)
	{
		Registered.Add((integrationId, originClientId, modal));
		LastModalId = $"modal-{Registered.Count}";

		return LastModalId;
	}

	public Task<ModalResult<JsonElement>> AwaitAsync(string modalId, CancellationToken cancellationToken)
		=> Task.FromResult(Answer is null
			? ModalResult.FromCancellation<JsonElement>()
			: ModalResult.FromValue(JsonSerializer.SerializeToElement(Answer)));

	public bool TryClaim(string modalId, string principal, out PendingModal modal)
	{
		modal = null!;

		return false;
	}

	public void BindSession(string modalId, string sessionId)
	{
	}

	public string? SessionFor(string modalId) => null;

	public bool Settle(string modalId, string principal, bool cancelled, JsonElement? value) => false;

	public void CancelInternal(string modalId)
	{
	}

	public void CancelForSession(string sessionId)
	{
	}

	public void CancelForClient(string clientId)
	{
	}

	public void SweepExpired()
	{
	}
}
